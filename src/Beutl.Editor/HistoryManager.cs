using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using Beutl.Editor.Observers;
using Beutl.Editor.Operations;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor;

public sealed class HistoryManager : IDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<HistoryManager>();
    private readonly Stack<HistoryTransaction> _undoStack = new();
    private readonly Stack<HistoryTransaction> _redoStack = new();
    private readonly OperationExecutionContext _context;
    private readonly OperationSequenceGenerator _sequenceGenerator;
    private readonly Subject<HistoryState> _stateChanged = new();
    private readonly Subject<System.Reactive.Unit> _beforeMutation = new();
    private readonly List<IDisposable> _subscriptions = new();
    private readonly object _lock = new();
    private readonly ObservableCollection<HistoryEntry> _entries = new();
    private readonly ReadOnlyObservableCollection<HistoryEntry> _readOnlyEntries;
    private long _transactionIdCounter;
    private HistoryTransaction _currentTransaction;
    private bool _isDisposed;

    public HistoryManager(CoreObject root, OperationSequenceGenerator sequenceGenerator)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
        _sequenceGenerator = sequenceGenerator ?? throw new ArgumentNullException(nameof(sequenceGenerator));
        _context = new OperationExecutionContext(root);
        _currentTransaction = new HistoryTransaction(Interlocked.Increment(ref _transactionIdCounter));
        _entries.Add(HistoryEntry.CreateInitial());
        _readOnlyEntries = new ReadOnlyObservableCollection<HistoryEntry>(_entries);
    }

    public CoreObject Root { get; }

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public int UndoCount => _undoStack.Count;

    public int RedoCount => _redoStack.Count;

    public IObservable<HistoryState> StateChanged => _stateChanged.AsObservable();

    // Undo / Redo / JumpTo の直前に発火する。debounce 等で未コミットの操作を抱えている
    // 購読側 (例: タイムラインの Nudge) が、ヒストリ操作に巻き込まれる前に
    // 自前で Commit を流し切るためのフック。
    public IObservable<System.Reactive.Unit> BeforeMutation => _beforeMutation.AsObservable();

    /// <summary>
    /// Fires <see cref="BeforeMutation"/> so subscribers (e.g. a debounced nudge service)
    /// flush any pending work into history now. Call this before deciding whether a
    /// following <see cref="Undo"/> / <see cref="Redo"/> / <see cref="JumpTo"/> will change
    /// scene state: those operations fire <see cref="BeforeMutation"/> themselves, so a
    /// still-pending edit is otherwise invisible to that decision yet gets committed — and
    /// possibly reverted — once the operation runs. The operation's own notification then
    /// finds nothing left to flush, so the flush stays single-effect for idempotent subscribers.
    /// </summary>
    public void FlushPendingMutations()
    {
        ThrowIfDisposed();
        FireBeforeMutation();
    }

    public ReadOnlyObservableCollection<HistoryEntry> Entries => _readOnlyEntries;

    public int CurrentIndex => _undoStack.Count;

    /// <summary>
    /// Whether the current uncommitted transaction holds operations, so a mutation can still
    /// change scene state even when <see cref="CanUndo"/> / <see cref="CanRedo"/> are false.
    /// </summary>
    public bool HasPendingOperations
    {
        get
        {
            ThrowIfDisposed();
            lock (_lock)
            {
                return _currentTransaction.HasOperations;
            }
        }
    }

    /// <summary>
    /// Returns a thread-safe snapshot of the current entries taken under the
    /// internal lock. Use this from threads other than the writer to avoid
    /// racing with concurrent <see cref="Commit"/>, <see cref="Clear"/>, or
    /// <see cref="JumpTo"/> mutations.
    /// </summary>
    public HistoryEntry[] GetEntriesSnapshot()
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            return [.. _entries];
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> if <see cref="JumpTo"/> with <paramref name="index"/>
    /// would mutate state — the index is in range and either differs from
    /// <see cref="CurrentIndex"/> or a pending transaction would be rolled back.
    /// </summary>
    public bool WouldJumpToMove(int index)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            if (index < 0 || index >= _entries.Count)
            {
                return false;
            }

            return index != _undoStack.Count || _currentTransaction.HasOperations;
        }
    }

    /// <summary>
    /// Atomically takes an initial snapshot and subscribes to subsequent
    /// <see cref="INotifyCollectionChanged.CollectionChanged"/> events on
    /// <see cref="Entries"/>. Use this when the consumer needs to mirror the
    /// collection without dropping events that fire between the snapshot and
    /// the subscription on a separate thread.
    /// </summary>
    /// <returns>
    /// A tuple containing the unsubscribe disposable, the initial snapshot,
    /// and the current index captured atomically with the snapshot.
    /// </returns>
    public (IDisposable Subscription, HistoryEntry[] InitialSnapshot, int InitialCurrentIndex) SubscribeEntries(
        NotifyCollectionChangedEventHandler handler)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(handler);

        INotifyCollectionChanged source = _entries;
        HistoryEntry[] snapshot;
        int currentIndex;
        lock (_lock)
        {
            snapshot = [.. _entries];
            currentIndex = _undoStack.Count;
            source.CollectionChanged += handler;
        }

        // Unsubscribe under the same lock that mutators hold so a concurrent
        // Commit/Clear/JumpTo cannot fire one last event after Dispose returns.
        var subscription = Disposable.Create(() =>
        {
            lock (_lock)
            {
                source.CollectionChanged -= handler;
            }
        });

        return (subscription, snapshot, currentIndex);
    }

    public void Commit(string? name = null, [CallerArgumentExpression(nameof(name))] string? expression = null)
    {
        ThrowIfDisposed();

        lock (_lock)
        {
            if (_currentTransaction.HasOperations)
            {
                _currentTransaction.Name = expression;
                _currentTransaction.DisplayName = name;
                _logger.LogDebug("Committing transaction: {TransactionName} (ID: {TransactionId}, Operations: {OperationCount})",
                    expression, _currentTransaction.Id, _currentTransaction.OperationCount);
                int currentEntryIndex = _undoStack.Count;
                _undoStack.Push(_currentTransaction);
                _redoStack.Clear();
                TruncateEntriesAfter(currentEntryIndex);
                _entries.Add(HistoryEntry.FromTransaction(_currentTransaction));
                _currentTransaction = new HistoryTransaction(Interlocked.Increment(ref _transactionIdCounter));
            }
            else
            {
                _logger.LogDebug("Commit called but no operations to commit");
            }
        }

        NotifyStateChanged();
    }

    public void Rollback()
    {
        ThrowIfDisposed();

        lock (_lock)
        {
            if (_currentTransaction.HasOperations)
            {
                _logger.LogDebug("Rolling back current transaction (ID: {TransactionId}, Operations: {OperationCount})",
                    _currentTransaction.Id, _currentTransaction.OperationCount);
                using (SuppressRecording())
                {
                    _currentTransaction.Revert(_context);
                }
            }

            _currentTransaction = new HistoryTransaction(Interlocked.Increment(ref _transactionIdCounter));
        }
    }

    public void Record(ChangeOperation operation)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(operation);

        lock (_lock)
        {
            if (RecordingSuppression.IsSuppressed)
            {
                _logger.LogDebug("Recording suppressed, ignoring operation: {OperationType}", operation.GetType().Name);
                return;
            }

            _currentTransaction.AddOperation(operation);
        }
    }

    public bool Undo()
    {
        ThrowIfDisposed();

        FireBeforeMutation();

        lock (_lock)
        {
            Rollback();

            if (_undoStack.Count == 0)
            {
                _logger.LogDebug("Undo requested but undo stack is empty");
                return false;
            }

            HistoryTransaction transaction = _undoStack.Pop();
            _logger.LogDebug("Undoing transaction: {TransactionName} (ID: {TransactionId})", transaction.Name, transaction.Id);

            using (SuppressRecording())
            {
                transaction.Revert(_context);
            }

            _redoStack.Push(transaction);
        }

        NotifyStateChanged();
        return true;
    }

    public bool Redo()
    {
        ThrowIfDisposed();

        FireBeforeMutation();

        lock (_lock)
        {
            Rollback();

            if (_redoStack.Count == 0)
            {
                _logger.LogDebug("Redo requested but redo stack is empty");
                return false;
            }

            HistoryTransaction transaction = _redoStack.Pop();
            _logger.LogDebug("Redoing transaction: {TransactionName} (ID: {TransactionId})", transaction.Name, transaction.Id);

            using (SuppressRecording())
            {
                transaction.Apply(_context);
            }

            _undoStack.Push(transaction);
        }

        NotifyStateChanged();
        return true;
    }

    public void Clear()
    {
        ThrowIfDisposed();

        lock (_lock)
        {
            int undoCount = _undoStack.Count;
            int redoCount = _redoStack.Count;
            _undoStack.Clear();
            _redoStack.Clear();

            // Avoid Clear() + Add(): a VM mirror on a different thread would
            // queue both events; the Reset path would resync to the already
            // re-added initial entry, and the queued Add would then duplicate
            // it. Emitting Replace + RemoveAt lets each event apply
            // independently without a Reset.
            HistoryEntry newInitial = HistoryEntry.CreateInitial();
            if (_entries.Count == 0)
            {
                _entries.Add(newInitial);
            }
            else
            {
                _entries[0] = newInitial;
                for (int i = _entries.Count - 1; i > 0; i--)
                {
                    _entries.RemoveAt(i);
                }
            }

            _logger.LogDebug("Cleared history stacks (Undo: {UndoCount}, Redo: {RedoCount})", undoCount, redoCount);
        }

        NotifyStateChanged();
    }

    public bool JumpTo(int index)
    {
        ThrowIfDisposed();

        FireBeforeMutation();

        bool moved = false;
        bool stateMutated = false;
        Exception? failure = null;

        try
        {
            lock (_lock)
            {
                if (index < 0 || index >= _entries.Count)
                {
                    _logger.LogDebug("JumpTo requested with out-of-range index: {Index} (Entries: {EntryCount})",
                        index, _entries.Count);
                    return false;
                }

                if (_currentTransaction.HasOperations)
                {
                    _logger.LogDebug("Rolling back current transaction before JumpTo");
                    // Always replace the current transaction even if Revert throws,
                    // otherwise the same operations would re-apply on the next commit.
                    try
                    {
                        using (SuppressRecording())
                        {
                            _currentTransaction.Revert(_context);
                        }
                    }
                    finally
                    {
                        _currentTransaction = new HistoryTransaction(Interlocked.Increment(ref _transactionIdCounter));
                        stateMutated = true;
                    }
                }

                try
                {
                    // Peek-then-pop preserves stack integrity if Revert/Apply throws:
                    // the failing transaction stays on the originating stack so it
                    // is not lost from history altogether.
                    while (_undoStack.Count > index)
                    {
                        HistoryTransaction transaction = _undoStack.Peek();
                        _logger.LogDebug("JumpTo undoing transaction: {TransactionName} (ID: {TransactionId})", transaction.Name, transaction.Id);
                        using (SuppressRecording())
                        {
                            transaction.Revert(_context);
                        }
                        _undoStack.Pop();
                        _redoStack.Push(transaction);
                        moved = true;
                        stateMutated = true;
                    }

                    while (_undoStack.Count < index && _redoStack.Count > 0)
                    {
                        HistoryTransaction transaction = _redoStack.Peek();
                        _logger.LogDebug("JumpTo redoing transaction: {TransactionName} (ID: {TransactionId})", transaction.Name, transaction.Id);
                        using (SuppressRecording())
                        {
                            transaction.Apply(_context);
                        }
                        _redoStack.Pop();
                        _undoStack.Push(transaction);
                        moved = true;
                        stateMutated = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "JumpTo failed at undo={UndoCount}, redo={RedoCount}, target={Target}; history is in a partial state",
                        _undoStack.Count, _redoStack.Count, index);
                    failure = ex;
                }

                // Forward loop exits when _redoStack is empty; if that happened
                // before reaching the requested index (e.g. residual stack drift
                // from a previously-failed step), do not silently report success.
                if (failure is null && _undoStack.Count != index)
                {
                    _logger.LogError(
                        "JumpTo could not reach target index {Target} (undo={UndoCount}, redo={RedoCount}); stacks are out of sync with entries",
                        index, _undoStack.Count, _redoStack.Count);
                    failure = new InvalidOperationException(
                        $"JumpTo could not reach target index {index} (undo={_undoStack.Count}, redo={_redoStack.Count}).");
                }
            }
        }
        finally
        {
            if (stateMutated)
            {
                NotifyStateChanged();
            }
        }

        if (failure is not null)
        {
            throw failure;
        }
        return moved;
    }

    private void TruncateEntriesAfter(int lastKeptIndex)
    {
        for (int i = _entries.Count - 1; i > lastKeptIndex; i--)
        {
            _entries.RemoveAt(i);
        }
    }

    public IDisposable Subscribe(IOperationObserver observer)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(observer);

        var subscription = observer.Operations.Subscribe(Record);
        lock (_subscriptions)
        {
            _subscriptions.Add(subscription);
        }
        return subscription;
    }

    public void ExecuteInTransaction(Action action, string? name = null, [CallerArgumentExpression(nameof(name))] string? expression = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            action();
            Commit(name, expression);
        }
        catch
        {
            Rollback();
            throw;
        }
    }

    public HistoryTransaction? PeekUndo()
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            return _undoStack.Count > 0 ? _undoStack.Peek() : null;
        }
    }

    public HistoryTransaction? PeekRedo()
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            return _redoStack.Count > 0 ? _redoStack.Peek() : null;
        }
    }

    public void Record(Action doAction, Action undoAction, string? description = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(doAction);
        ArgumentNullException.ThrowIfNull(undoAction);

        var operation = CustomOperation.Create(doAction, undoAction, _sequenceGenerator, description);
        Record(operation);
    }

    public RecordingScope<TState> BeginRecordingScope<TState>(
        Func<TState> captureState,
        Action<TState> applyState,
        string? description = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(captureState);
        ArgumentNullException.ThrowIfNull(applyState);

        return new RecordingScope<TState>(this, captureState, applyState, _sequenceGenerator, description);
    }

    private void NotifyStateChanged()
    {
        _stateChanged.OnNext(new HistoryState(CanUndo, CanRedo, UndoCount, RedoCount));
    }

    // BeforeMutation subscribers are user-supplied (e.g. timeline flush handlers).
    // A throw must not abort the Undo/Redo that triggered the notification, since
    // the history operation itself is independent of any debounce flush.
    private void FireBeforeMutation()
    {
        try
        {
            _beforeMutation.OnNext(System.Reactive.Unit.Default);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BeforeMutation subscriber threw; continuing with the pending Undo/Redo.");
        }
    }

    public IDisposable SuppressRecording()
    {
        ThrowIfDisposed();
        return RecordingSuppression.Enter();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        lock (_subscriptions)
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }
            _subscriptions.Clear();
        }
        _stateChanged.OnCompleted();
        _stateChanged.Dispose();
        _beforeMutation.OnCompleted();
        _beforeMutation.Dispose();
        _undoStack.Clear();
        _redoStack.Clear();
    }
}

public readonly record struct HistoryState(bool CanUndo, bool CanRedo, int UndoCount, int RedoCount);
