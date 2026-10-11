using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using Beutl.Editor.Observers;
using Beutl.Editor.Operations;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor;

public sealed partial class HistoryManager : IDisposable
{
    private const int MaximumPreActionPublicationTransactions = 64;
    private readonly ILogger _logger = Log.CreateLogger<HistoryManager>();
    private readonly Stack<HistoryTransaction> _undoStack = new();
    private readonly Stack<HistoryTransaction> _redoStack = new();
    private readonly OperationExecutionContext _context;
    private readonly OperationSequenceGenerator _sequenceGenerator;
    private readonly FaultIsolatedSubject<HistoryState> _stateChanged;
    private readonly FaultIsolatedSubject<System.Reactive.Unit> _beforeMutation;
    private readonly List<IDisposable> _subscriptions = new();
    private readonly List<EntrySubscriber> _entrySubscribers = [];
    private readonly object _lock = new();
    private readonly ObservableCollection<HistoryEntry> _entries = new();
    private readonly ReadOnlyObservableCollection<HistoryEntry> _readOnlyEntries;
    private long _transactionIdCounter;
    private HistoryTransaction _currentTransaction;
    private int _minimumReplayIndex;
    private int _maximumReplayIndex = int.MaxValue;
    private int _entryPublicationDepth;
    private readonly AsyncLocal<BeforeMutationDispatch?> _beforeMutationDispatch = new();
    private bool _isolatedTransactionActive;
    private bool _isDisposed;

    public HistoryManager(CoreObject root, OperationSequenceGenerator sequenceGenerator)
    {
        _stateChanged = new FaultIsolatedSubject<HistoryState>(ex =>
            _logger.LogError(ex, "A history state observer failed; continuing publication."));
        _beforeMutation = new FaultIsolatedSubject<System.Reactive.Unit>(ex =>
            _logger.LogError(ex, "A BeforeMutation observer failed; continuing publication."));
        Root = root ?? throw new ArgumentNullException(nameof(root));
        _sequenceGenerator = sequenceGenerator ?? throw new ArgumentNullException(nameof(sequenceGenerator));
        _context = new OperationExecutionContext(root);
        _currentTransaction = CreateTransaction();
        _entries.Add(HistoryEntry.CreateInitial());
        _readOnlyEntries = new IsolatedEntryCollection(_entries, _logger);
    }

    public CoreObject Root { get; }

    public bool CanUndo => _undoStack.Count > _minimumReplayIndex;
    public bool CanRedo => _redoStack.Count > 0 && _undoStack.Count < _maximumReplayIndex;

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

    // Read synchronously from an Entries Add observer. Keep HistoryEntry itself
    // lightweight: UI snapshots must not retain scene graphs through operations.
    internal IReadOnlyList<ChangeOperation> GetLatestCommittedOperations(long? transactionId)
    {
        lock (_lock)
        {
            return _undoStack.TryPeek(out HistoryTransaction? transaction) && transaction.Id == transactionId
                ? transaction.Operations : [];
        }
    }

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
    /// would mutate state — the index is reachable and either differs from
    /// <see cref="CurrentIndex"/> or a pending transaction would be rolled back.
    /// </summary>
    public bool WouldJumpToMove(int index)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            if (!IsReplayIndexReachable_NoLock(index))
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

        var subscriber = new EntrySubscriber(handler);
        HistoryEntry[] snapshot;
        int currentIndex;
        lock (_lock)
        {
            snapshot = [.. _entries];
            currentIndex = _undoStack.Count;
            _entrySubscribers.Add(subscriber);
        }

        // Unsubscribe under the same lock that mutators hold so a concurrent
        // Commit/Clear/JumpTo cannot fire one last event after Dispose returns.
        var subscription = Disposable.Create(() =>
        {
            lock (_lock)
            {
                _entrySubscribers.Remove(subscriber);
            }
        });

        return (subscription, snapshot, currentIndex);
    }

    public void Commit(string? name = null, [CallerArgumentExpression(nameof(name))] string? expression = null)
    {
        ThrowIfDisposed();

        lock (_lock)
        {
            ThrowIfHistoryControlIsBlocked_NoLock();
            CommitCurrentTransaction_NoLock(name, expression);
        }

        NotifyStateChanged();
    }

    public void Rollback()
    {
        ThrowIfDisposed();

        bool attemptedMutation = false;
        try
        {
            lock (_lock)
            {
                ThrowIfHistoryControlIsBlocked_NoLock();
                attemptedMutation = _currentTransaction.HasOperations;
                RollbackCurrentTransaction_NoLock();
            }
        }
        finally
        {
            if (attemptedMutation)
                NotifyStateChanged();
        }
    }

    /// <summary>
    /// Commits already pending operations as a separate history entry, then executes and commits
    /// <paramref name="action"/> as one isolated transaction.
    /// </summary>
    /// <remarks>
    /// The action is synchronous so every observer it triggers records reentrantly while the
    /// history gate is held. Records from other threads wait until the action commits or rolls back.
    /// Records produced while earlier pending entries are published are committed as separate
    /// entries before the callback begins.
    /// <paramref name="cancellationToken"/> is checked before mutation flush and again after the
    /// gate is acquired, before any pending transaction is committed; the callback owns any later
    /// cancellation checks it requires.
    /// The action must not call <see cref="Commit"/>, <see cref="Rollback"/>, <see cref="Undo"/>,
    /// <see cref="Redo"/>, <see cref="Clear"/>, <see cref="JumpTo"/>, or this method recursively.
    /// </remarks>
    public void ExecuteInTransaction(
        Action action,
        string? name = null,
        [CallerArgumentExpression(nameof(name))] string? expression = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        FireBeforeMutation();

        bool stateChanged = false;
        try
        {
            lock (_lock)
            {
                ThrowIfHistoryControlIsBlocked_NoLock();
                cancellationToken.ThrowIfCancellationRequested();
                _isolatedTransactionActive = true;
                try
                {
                    stateChanged |= CommitCurrentTransaction_NoLock(name: null, expression: null);
                    DrainPreActionPublicationTransactions_NoLock(ref stateChanged);
                    try
                    {
                        action();
                        stateChanged |= CommitCurrentTransaction_NoLock(name, expression);
                    }
                    catch (Exception actionFailure)
                    {
                        try
                        {
                            RollbackIsolatedTransaction_NoLock();
                        }
                        catch (Exception rollbackFailure)
                        {
                            throw new AggregateException(
                                "An isolated history transaction and its rollback both failed.",
                                [actionFailure, .. GetRollbackFailures(rollbackFailure)]);
                        }
                        throw;
                    }
                }
                finally
                {
                    _isolatedTransactionActive = false;
                }
            }
        }
        finally
        {
            if (stateChanged)
                NotifyStateChanged();
        }
    }

    private void ThrowIfHistoryControlIsBlocked_NoLock()
    {
        if (_isolatedTransactionActive)
        {
            throw new InvalidOperationException(
                "History cannot commit or roll back from inside an isolated transaction.");
        }
        if (_entryPublicationDepth > 0)
        {
            throw new InvalidOperationException(
                "History control cannot run while history entry changes are being published.");
        }
    }

    private HistoryTransaction CreateTransaction()
        => new(Interlocked.Increment(ref _transactionIdCounter));

    private bool CommitCurrentTransaction_NoLock(string? name, string? expression)
    {
        if (!_currentTransaction.HasOperations)
        {
            _logger.LogDebug("Commit called but no operations to commit");
            return false;
        }

        HistoryTransaction transaction = _currentTransaction;
        transaction.Name = expression;
        transaction.DisplayName = name;
        _logger.LogDebug(
            "Committing transaction: {TransactionName} (ID: {TransactionId}, Operations: {OperationCount})",
            expression,
            transaction.Id,
            transaction.OperationCount);
        int currentEntryIndex = _undoStack.Count;
        _undoStack.Push(transaction);
        _redoStack.Clear();
        // A new branch can replay its own edits, but cannot undo past an
        // earlier failure whose surviving model state is uncertain.
        _maximumReplayIndex = int.MaxValue;
        _currentTransaction = CreateTransaction();
        TruncateEntriesAfter(currentEntryIndex);
        AddEntry(HistoryEntry.FromTransaction(transaction));
        return true;
    }

    private void DrainPreActionPublicationTransactions_NoLock(ref bool stateChanged)
    {
        for (int count = 0; _currentTransaction.HasOperations; count++)
        {
            if (count >= MaximumPreActionPublicationTransactions)
            {
                throw new InvalidOperationException(
                    "History entry publication did not reach a stable transaction boundary.");
            }

            stateChanged |= CommitCurrentTransaction_NoLock(name: null, expression: null);
        }
    }

    private void RollbackCurrentTransaction_NoLock()
    {
        if (_currentTransaction.HasOperations)
        {
            _logger.LogDebug(
                "Rolling back current transaction (ID: {TransactionId}, Operations: {OperationCount})",
                _currentTransaction.Id,
                _currentTransaction.OperationCount);
            ReplayTransaction_NoLock(_currentTransaction, redo: false);
        }

        _currentTransaction = CreateTransaction();
    }

    private void RollbackIsolatedTransaction_NoLock()
    {
        HistoryTransaction transaction = _currentTransaction;
        _currentTransaction = CreateTransaction();
        if (transaction.HasOperations)
        {
            _logger.LogDebug(
                "Rolling back isolated transaction (ID: {TransactionId}, Operations: {OperationCount})",
                transaction.Id,
                transaction.OperationCount);
            List<Exception>? failures = null;
            using (SuppressRecording())
            {
                for (int i = transaction.Operations.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        transaction.Operations[i].Revert(_context);
                    }
                    catch (Exception ex)
                    {
                        (failures ??= []).Add(ex);
                    }
                }
            }

            if (failures is [var failure])
                throw failure;
            if (failures is { Count: > 1 })
                throw new AggregateException("One or more history rollback operations failed.", failures);
        }
    }

    private static IEnumerable<Exception> GetRollbackFailures(Exception failure)
    {
        return failure is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions
            : [failure];
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

    public bool Undo() => MoveHistory(redo: false);

    public bool Redo() => MoveHistory(redo: true);

    private bool MoveHistory(bool redo)
    {
        ThrowIfDisposed();
        FireBeforeMutation();
        bool attemptedMutation = false;
        try
        {
            lock (_lock)
            {
                ThrowIfHistoryControlIsBlocked_NoLock();
                attemptedMutation = _currentTransaction.HasOperations;
                RollbackCurrentTransaction_NoLock();
                Stack<HistoryTransaction> source = redo ? _redoStack : _undoStack;
                if (!(redo ? CanRedo : CanUndo))
                    return false;

                HistoryTransaction transaction = source.Peek();
                _logger.LogDebug("{Action} transaction: {TransactionName} (ID: {TransactionId})",
                    redo ? "Redoing" : "Undoing", transaction.Name, transaction.Id);
                attemptedMutation = true;
                ReplayTopTransaction_NoLock(transaction, redo);
                return true;
            }
        }
        finally
        {
            // A failing operation can already have changed the model. Publish the
            // surviving state even when replay was stopped at a failure boundary.
            if (attemptedMutation)
                NotifyStateChanged();
        }
    }

    public void Clear()
    {
        ThrowIfDisposed();

        lock (_lock)
        {
            ThrowIfHistoryControlIsBlocked_NoLock();
            ClearHistory_NoLock();
        }

        NotifyStateChanged();
    }

    private void ClearHistory_NoLock()
    {
        int undoCount = _undoStack.Count;
        int redoCount = _redoStack.Count;
        _undoStack.Clear();
        _redoStack.Clear();
        _minimumReplayIndex = 0;
        _maximumReplayIndex = int.MaxValue;

        // Avoid Clear() + Add(): a VM mirror on a different thread would
        // queue both events; the Reset path would resync to the already
        // re-added initial entry, and the queued Add would then duplicate
        // it. Emitting Replace + RemoveAt lets each event apply
        // independently without a Reset.
        HistoryEntry newInitial = HistoryEntry.CreateInitial();
        if (_entries.Count == 0)
        {
            AddEntry(newInitial);
        }
        else
        {
            ReplaceEntry(0, newInitial);
            for (int i = _entries.Count - 1; i > 0; i--)
            {
                RemoveEntryAt(i);
            }
        }

        _logger.LogDebug("Cleared history stacks (Undo: {UndoCount}, Redo: {RedoCount})", undoCount, redoCount);
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
                ThrowIfHistoryControlIsBlocked_NoLock();
                if (!IsReplayIndexReachable_NoLock(index))
                {
                    _logger.LogDebug("JumpTo requested with unreachable index: {Index} (Entries: {EntryCount})",
                        index, _entries.Count);
                    return false;
                }

                if (_currentTransaction.HasOperations)
                {
                    _logger.LogDebug("Rolling back current transaction before JumpTo");
                    // The outer finally publishes the state even when the revert throws.
                    stateMutated = true;
                    DiscardPendingTransaction_NoLock();
                }

                try
                {
                    while (_undoStack.Count > index)
                    {
                        HistoryTransaction transaction = _undoStack.Peek();
                        _logger.LogDebug("JumpTo undoing transaction: {TransactionName} (ID: {TransactionId})", transaction.Name, transaction.Id);
                        stateMutated = true;
                        ReplayTopTransaction_NoLock(transaction, redo: false);
                        moved = true;
                    }

                    while (_undoStack.Count < index && _redoStack.Count > 0)
                    {
                        HistoryTransaction transaction = _redoStack.Peek();
                        _logger.LogDebug("JumpTo redoing transaction: {TransactionName} (ID: {TransactionId})", transaction.Name, transaction.Id);
                        stateMutated = true;
                        ReplayTopTransaction_NoLock(transaction, redo: true);
                        moved = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "JumpTo failed at undo={UndoCount}, redo={RedoCount}, target={Target}",
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

    // Always replace the current transaction even if Revert throws,
    // otherwise the same operations would re-apply on the next commit.
    private void DiscardPendingTransaction_NoLock()
    {
        try
        {
            ReplayTransaction_NoLock(_currentTransaction, redo: false);
        }
        finally
        {
            _currentTransaction = CreateTransaction();
        }
    }

    private bool IsReplayIndexReachable_NoLock(int index)
        => index >= 0 && index < _entries.Count
           && index >= _minimumReplayIndex && index <= _maximumReplayIndex;

    // Both retryable and uncertain failures retain the transaction on its
    // originating stack; only retryable failures may be replayed again.
    private void ReplayTopTransaction_NoLock(HistoryTransaction transaction, bool redo)
    {
        ReplayTransaction_NoLock(transaction, redo);

        Stack<HistoryTransaction> source = redo ? _redoStack : _undoStack;
        Stack<HistoryTransaction> destination = redo ? _undoStack : _redoStack;
        source.Pop();
        destination.Push(transaction);
    }

    private void ReplayTransaction_NoLock(HistoryTransaction transaction, bool redo)
    {
        try
        {
            using (SuppressRecording())
            {
                if (redo)
                    transaction.Apply(_context);
                else
                    transaction.Revert(_context);
            }
        }
        catch (Exception ex) when (transaction.HasUncertainFailure)
        {
            // Preserve all committed history, but neither direction can safely
            // replay from this uncertain model state. New edits start above this
            // boundary and remain undoable without revisiting the failed operation.
            _logger.LogError(ex, "History replay partially failed; preserving history and blocking replay across the failure boundary.");
            _minimumReplayIndex = _undoStack.Count;
            _maximumReplayIndex = _undoStack.Count;
            if (ReferenceEquals(transaction, _currentTransaction))
                _currentTransaction = CreateTransaction();
            throw new HistoryReplayException(ex);
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
        if (_beforeMutationDispatch.Value is { IsActive: true })
            return;
        BeforeMutationDispatch? previous = _beforeMutationDispatch.Value;
        var dispatch = new BeforeMutationDispatch();
        _beforeMutationDispatch.Value = dispatch;
        try
        {
            _beforeMutation.OnNext(System.Reactive.Unit.Default);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BeforeMutation subscriber threw; continuing with the pending Undo/Redo.");
        }
        finally
        {
            dispatch.Complete();
            _beforeMutationDispatch.Value = previous;
        }
    }

    private sealed class BeforeMutationDispatch
    {
        private int _active = 1;
        public bool IsActive => Volatile.Read(ref _active) != 0;
        public void Complete() => Volatile.Write(ref _active, 0);
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
        lock (_lock)
        {
            _entrySubscribers.Clear();
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
