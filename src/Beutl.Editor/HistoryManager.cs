using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace Beutl.Editor;

public sealed class HistoryManager : IDisposable
{
    private readonly Stack<ChangeOperation> _undoStack = new();
    private readonly Stack<ChangeOperation> _redoStack = new();
    private readonly OperationExecutionContext _context;
    private readonly OperationSequenceGenerator _sequenceGenerator;
    private readonly Subject<HistoryState> _stateChanged = new();
    private readonly List<IDisposable> _subscriptions = new();
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

    public IObservable<HistoryState> StateChanged => _stateChanged.AsObservable();

    public void Record(ChangeOperation operation)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(operation);

        _undoStack.Push(operation);
        _redoStack.Clear();
        NotifyStateChanged();
    }

    public bool Undo()
    {
        ThrowIfDisposed();

        if (_undoStack.Count == 0)
            return false;

        var operation = _undoStack.Pop();
        var revertOperation = operation.CreateRevertOperation(_context, _sequenceGenerator);

        using (PublishingSuppression.Enter())
        {
            revertOperation.Apply(_context);
        }

        _redoStack.Push(revertOperation);
        NotifyStateChanged();
        return true;
    }

    public bool Redo()
    {
        ThrowIfDisposed();

        if (_redoStack.Count == 0)
            return false;

        var operation = _redoStack.Pop();
        var revertOperation = operation.CreateRevertOperation(_context, _sequenceGenerator);

        using (PublishingSuppression.Enter())
        {
            revertOperation.Apply(_context);
        }

        _undoStack.Push(revertOperation);
        NotifyStateChanged();
        return true;
    }

    public void Clear()
    {
        ThrowIfDisposed();

        _undoStack.Clear();
        _redoStack.Clear();
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
    }
}

public readonly record struct HistoryState(bool CanUndo, bool CanRedo, int UndoCount, int RedoCount);
