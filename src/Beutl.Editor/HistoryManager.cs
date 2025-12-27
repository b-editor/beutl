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
    private readonly List<IDisposable> _subscriptions = new();
    private readonly object _lock = new();
    private long _transactionIdCounter;
    private HistoryTransaction _currentTransaction;
    private bool _isDisposed;

    public HistoryManager(CoreObject root, OperationSequenceGenerator sequenceGenerator)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
        _sequenceGenerator = sequenceGenerator ?? throw new ArgumentNullException(nameof(sequenceGenerator));
        _context = new OperationExecutionContext(root);
        _currentTransaction = new HistoryTransaction(Interlocked.Increment(ref _transactionIdCounter));
    }

    public CoreObject Root { get; }

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public int UndoCount => _undoStack.Count;

    public int RedoCount => _redoStack.Count;

    public IObservable<HistoryState> StateChanged => _stateChanged.AsObservable();

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
                _undoStack.Push(_currentTransaction);
                _redoStack.Clear();
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
            _logger.LogDebug("Cleared history stacks (Undo: {UndoCount}, Redo: {RedoCount})", undoCount, redoCount);
        }

        NotifyStateChanged();
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
        _undoStack.Clear();
        _redoStack.Clear();
    }
}

public readonly record struct HistoryState(bool CanUndo, bool CanRedo, int UndoCount, int RedoCount);
