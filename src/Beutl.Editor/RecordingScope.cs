using Beutl.Editor.Operations;

namespace Beutl.Editor;

public sealed class RecordingScope<TState> : IDisposable
{
    private readonly HistoryManager _historyManager;
    private readonly Func<TState> _captureState;
    private readonly Action<TState> _applyState;
    private readonly OperationSequenceGenerator _sequenceGenerator;
    private readonly string? _description;
    private readonly TState _beforeState;
    private bool _completed;
    private bool _disposed;

    internal RecordingScope(
        HistoryManager historyManager,
        Func<TState> captureState,
        Action<TState> applyState,
        OperationSequenceGenerator sequenceGenerator,
        string? description)
    {
        _historyManager = historyManager;
        _captureState = captureState;
        _applyState = applyState;
        _sequenceGenerator = sequenceGenerator;
        _description = description;
        _beforeState = captureState();
    }

    public void Complete()
    {
        if (_completed || _disposed)
            return;

        _completed = true;
        var afterState = _captureState();
        var operation = CreateStateOperation(_beforeState, afterState);
        _historyManager.Record(operation);
    }

    public void Cancel()
    {
        _disposed = true;
    }

    private CustomOperation CreateStateOperation(TState fromState, TState toState)
    {
        return new CustomOperation(
            _ => _applyState(toState),
            _ => _applyState(fromState),
            _description) { SequenceNumber = _sequenceGenerator.GetNext() };
    }

    public void Dispose()
    {
        if (!_disposed && !_completed)
        {
            // Auto-complete on dispose if not cancelled
            Complete();
        }

        _disposed = true;
    }
}
