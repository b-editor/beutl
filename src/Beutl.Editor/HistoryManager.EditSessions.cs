using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor;

// One edit session at a time owns its undo entry; writers that must not split it wait in a queue (#2874).
public sealed partial class HistoryManager
{
    // Apart from _lock, which an isolated transaction can hold for long on another thread.
    private readonly object _queueLock = new();
    private readonly Queue<QueuedEdit> _queuedEdits = new();
    private EditSession? _activeSession;
    private int _sessionScopeThread;
    private int _sessionScopeDepth;
    private bool _drainingQueuedEdits;

    public bool IsEditing => Volatile.Read(ref _activeSession) is not null;

    internal int QueuedEditCount
    {
        get
        {
            lock (_queueLock)
            {
                return _queuedEdits.Count;
            }
        }
    }

    public EditSession BeginEdit(string? name = null, [CallerArgumentExpression(nameof(name))] string? expression = null)
        => BeginEditCore(name, expression, yieldsToCommit: false);

    internal EditSession BeginYieldingEdit(string? name, [CallerArgumentExpression(nameof(name))] string? expression = null)
        => BeginEditCore(name, expression, yieldsToCommit: true);

    private EditSession BeginEditCore(string? name, string? expression, bool yieldsToCommit)
    {
        ThrowIfDisposed();
        // Listeners such as the nudge window complete their own session first.
        FireBeforeMutation();

        bool committed;
        EditSession session;
        lock (_lock)
        {
            ThrowIfHistoryControlIsBlocked_NoLock();
            if (_activeSession is not null)
                throw new InvalidOperationException("Another edit session is open.");

            // Earlier pending edits get their own entry, so undo replays them in the order they were made.
            committed = CommitCurrentTransaction_NoLock(name: null, expression: null);
            session = new EditSession(this, CreateTransaction(), name, expression, yieldsToCommit);
            _activeSession = session;
        }

        if (committed)
            NotifyStateChanged();
        return session;
    }

    // Runs edit now when no session is open, otherwise after it closes, in call order. Call on the UI thread.
    public Task RunAfterEditAsync(Action edit, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(edit);
        cancellationToken.ThrowIfCancellationRequested();

        var queued = new QueuedEdit(edit);
        lock (_queueLock)
        {
            if (IsEditing || _queuedEdits.Count > 0)
            {
                _queuedEdits.Enqueue(queued);
                if (cancellationToken.CanBeCanceled)
                    queued.Registration = cancellationToken.Register(() => queued.Completion.TrySetCanceled(cancellationToken));
                return queued.Completion.Task;
            }
        }

        queued.Run();
        return queued.Completion.Task;
    }

    internal IDisposable EnterSession(EditSession session)
    {
        lock (_lock)
        {
            ThrowIfNotActive_NoLock(session);
            int thread = Environment.CurrentManagedThreadId;
            if (_sessionScopeDepth > 0 && _sessionScopeThread != thread)
                throw new InvalidOperationException("The edit session is entered on another thread.");

            _sessionScopeThread = thread;
            _sessionScopeDepth++;
        }

        return new SessionScope(this, session);
    }

    internal void CompleteSession(EditSession session)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            ThrowIfNotActive_NoLock(session);
            ThrowIfHistoryControlIsBlocked_NoLock();
            _activeSession = null;
            if (session.Transaction.HasOperations)
                PushTransaction_NoLock(session.Transaction, session.Name, session.Expression);
        }

        if (session.Transaction.HasOperations)
            NotifyStateChanged();
        DrainQueuedEdits();
    }

    internal void CancelSession(EditSession session)
    {
        ThrowIfDisposed();
        bool stateChanged = false;
        try
        {
            lock (_lock)
            {
                ThrowIfNotActive_NoLock(session);
                ThrowIfHistoryControlIsBlocked_NoLock();
                _activeSession = null;
                stateChanged = session.Transaction.HasOperations;
                RevertDetachedTransaction_NoLock(session.Transaction, ref stateChanged);
            }
        }
        finally
        {
            if (stateChanged)
                NotifyStateChanged();
            DrainQueuedEdits();
        }
    }

    internal bool TryCancelGesture()
    {
        if (Volatile.Read(ref _activeSession)?.CancelRequested is not { } cancel)
            return false;

        cancel();
        return true;
    }

    internal bool IsSessionOpen(EditSession session)
    {
        lock (_lock)
        {
            return ReferenceEquals(_activeSession, session);
        }
    }

    private bool IsInSessionScope_NoLock()
        => _activeSession is not null
           && _sessionScopeDepth > 0
           && _sessionScopeThread == Environment.CurrentManagedThreadId;

    private bool IsEditSessionOpen_NoLock(string operation)
    {
        if (_activeSession is null)
            return false;

        _logger.LogDebug("{Operation} skipped: the edit session {Session} is open.", operation, _activeSession.Expression);
        return true;
    }

    private void ThrowIfNotActive_NoLock(EditSession session)
    {
        if (!ReferenceEquals(_activeSession, session))
            throw new InvalidOperationException("The edit session is closed.");
    }

    // Completes a yielding session ahead of a commit of edits made outside it.
    private bool CompleteYieldingSessionBeforeCommit_NoLock()
    {
        if (_activeSession is not { YieldsToCommit: true } session || !_currentTransaction.HasOperations)
            return false;

        _activeSession = null;
        if (session.Transaction.HasOperations)
            PushTransaction_NoLock(session.Transaction, session.Name, session.Expression);
        return true;
    }

    private void DrainQueuedEdits()
    {
        // A session completed by BeforeMutation must not let writers in before the history move that fired it.
        if (_drainingQueuedEdits || _beforeMutationDispatch.Value is { IsActive: true })
            return;

        _drainingQueuedEdits = true;
        try
        {
            while (true)
            {
                QueuedEdit next;
                lock (_queueLock)
                {
                    if (_isDisposed || IsEditing || !_queuedEdits.TryDequeue(out next!))
                        return;
                }

                next.Run();
            }
        }
        finally
        {
            _drainingQueuedEdits = false;
        }
    }

    private void DropEditSessions()
    {
        QueuedEdit[] dropped;
        lock (_lock)
        {
            _activeSession = null;
            _sessionScopeDepth = 0;
        }

        lock (_queueLock)
        {
            dropped = [.. _queuedEdits];
            _queuedEdits.Clear();
        }

        foreach (QueuedEdit edit in dropped)
            edit.Completion.TrySetException(new ObjectDisposedException(nameof(HistoryManager)));
    }

    private sealed class SessionScope(HistoryManager owner, EditSession session) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            lock (owner._lock)
            {
                if (ReferenceEquals(owner._activeSession, session) && owner._sessionScopeDepth > 0)
                    owner._sessionScopeDepth--;
            }
        }
    }

    private sealed class QueuedEdit(Action edit)
    {
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationTokenRegistration Registration { get; set; }

        public void Run()
        {
            Registration.Dispose();
            if (Completion.Task.IsCompleted)
                return;

            try
            {
                edit();
                Completion.TrySetResult();
            }
            catch (Exception ex)
            {
                Completion.TrySetException(ex);
            }
        }
    }
}
