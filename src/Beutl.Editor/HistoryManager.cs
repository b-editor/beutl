using System.Reactive.Linq;
using System.Reactive.Subjects;
using Beutl.Editor.Infrastructure;
using Beutl.Editor.Observers;
using Beutl.Editor.Operations;

namespace Beutl.Editor;

public sealed class HistoryManager : IDisposable
{
    private readonly Stack<HistoryTransaction> _undoStack = new();
    private readonly Stack<HistoryTransaction> _redoStack = new();
    private readonly OperationExecutionContext _context;
    private readonly OperationSequenceGenerator _sequenceGenerator;
    private readonly Subject<HistoryState> _stateChanged = new();
    private readonly List<IDisposable> _subscriptions = new();
    private readonly object _lock = new();
    private long _transactionIdCounter;
    private HistoryTransaction? _currentTransaction;
    private bool _isDisposed;

    public HistoryManager(CoreObject root, OperationSequenceGenerator sequenceGenerator)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
        _sequenceGenerator = sequenceGenerator ?? throw new ArgumentNullException(nameof(sequenceGenerator));
        _context = new OperationExecutionContext(root);
    }

    public CoreObject Root { get; }

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public int UndoCount => _undoStack.Count;

    public int RedoCount => _redoStack.Count;

    public bool IsTransactionInProgress => _currentTransaction != null;

    public IObservable<HistoryState> StateChanged => _stateChanged.AsObservable();

    public HistoryTransaction BeginTransaction(string? name = null)
    {
        ThrowIfDisposed();

        lock (_lock)
        {
            if (_currentTransaction != null)
            {
                throw new InvalidOperationException("A transaction is already in progress. Commit or rollback the current transaction before starting a new one.");
            }

            _currentTransaction = new HistoryTransaction(Interlocked.Increment(ref _transactionIdCounter), name);
            return _currentTransaction;
        }
    }

    public void Commit()
    {
        ThrowIfDisposed();

        lock (_lock)
        {
            if (_currentTransaction == null)
            {
                throw new InvalidOperationException("No transaction is in progress.");
            }

            if (_currentTransaction.HasOperations)
            {
                _undoStack.Push(_currentTransaction);
                _redoStack.Clear();
            }

            _currentTransaction = null;
        }

        NotifyStateChanged();
    }

    public void Rollback()
    {
        ThrowIfDisposed();

        lock (_lock)
        {
            if (_currentTransaction == null)
            {
                throw new InvalidOperationException("No transaction is in progress.");
            }

            if (_currentTransaction.HasOperations)
            {
                // Create and apply revert operations
                var revertTransaction = _currentTransaction.CreateRevertTransaction(
                    _context, _sequenceGenerator, Interlocked.Increment(ref _transactionIdCounter));

                using (PublishingSuppression.Enter())
                {
                    revertTransaction.Apply(_context);
                }
            }

            _currentTransaction = null;
        }
    }

    public void Record(ChangeOperation operation)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(operation);

        lock (_lock)
        {
            if (_currentTransaction != null)
            {
                // Add to current transaction
                _currentTransaction.AddOperation(operation);
            }
            else
            {
                // Create an implicit single-operation transaction
                var transaction = new HistoryTransaction(Interlocked.Increment(ref _transactionIdCounter));
                transaction.AddOperation(operation);
                _undoStack.Push(transaction);
                _redoStack.Clear();
                NotifyStateChanged();
            }
        }
    }
    
    public bool Undo()
    {
        ThrowIfDisposed();

        HistoryTransaction? transaction;
        HistoryTransaction? revertTransaction;

        lock (_lock)
        {
            if (_currentTransaction != null)
            {
                throw new InvalidOperationException("Cannot undo while a transaction is in progress. Commit or rollback first.");
            }

            if (_undoStack.Count == 0)
                return false;

            transaction = _undoStack.Pop();
            revertTransaction = transaction.CreateRevertTransaction(
                _context, _sequenceGenerator, Interlocked.Increment(ref _transactionIdCounter));

            using (PublishingSuppression.Enter())
            {
                revertTransaction.Apply(_context);
            }

            _redoStack.Push(revertTransaction);
        }

        NotifyStateChanged();
        return true;
    }

    public bool Redo()
    {
        ThrowIfDisposed();

        HistoryTransaction? transaction;
        HistoryTransaction? revertTransaction;

        lock (_lock)
        {
            if (_currentTransaction != null)
            {
                throw new InvalidOperationException("Cannot redo while a transaction is in progress. Commit or rollback first.");
            }

            if (_redoStack.Count == 0)
                return false;

            transaction = _redoStack.Pop();
            revertTransaction = transaction.CreateRevertTransaction(
                _context, _sequenceGenerator, Interlocked.Increment(ref _transactionIdCounter));

            using (PublishingSuppression.Enter())
            {
                revertTransaction.Apply(_context);
            }

            _undoStack.Push(revertTransaction);
        }

        NotifyStateChanged();
        return true;
    }

    public void Clear()
    {
        ThrowIfDisposed();

        lock (_lock)
        {
            if (_currentTransaction != null)
            {
                throw new InvalidOperationException("Cannot clear history while a transaction is in progress.");
            }

            _undoStack.Clear();
            _redoStack.Clear();
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

    public void ExecuteInTransaction(Action action, string? name = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(action);

        BeginTransaction(name);
        try
        {
            action();
            Commit();
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

    private void NotifyStateChanged()
    {
        _stateChanged.OnNext(new HistoryState(CanUndo, CanRedo, UndoCount, RedoCount));
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
        _currentTransaction = null;
    }
}

public readonly record struct HistoryState(bool CanUndo, bool CanRedo, int UndoCount, int RedoCount);
