namespace Beutl.Editor.Operations;

public sealed class CustomOperation : ChangeOperation
{
    private readonly Action<OperationExecutionContext> _apply;
    private readonly Action<OperationExecutionContext> _revert;

    public CustomOperation(
        Action<OperationExecutionContext> apply,
        Action<OperationExecutionContext> revert,
        string? description = null)
    {
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _revert = revert ?? throw new ArgumentNullException(nameof(revert));
        Description = description;
    }

    public string? Description { get; }

    public override void Apply(OperationExecutionContext context)
    {
        _apply(context);
    }

    public override void Revert(OperationExecutionContext context)
    {
        _revert(context);
    }

    public static CustomOperation Create(
        Action doAction,
        Action undoAction,
        OperationSequenceGenerator sequenceGenerator,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(doAction);
        ArgumentNullException.ThrowIfNull(undoAction);
        ArgumentNullException.ThrowIfNull(sequenceGenerator);

        return new CustomOperation(
            _ => doAction(),
            _ => undoAction(),
            description) { SequenceNumber = sequenceGenerator.GetNext() };
    }

    public static StateCapturingOperationBuilder<TState> CaptureState<TState>(
        Func<TState> captureState,
        Action<TState> applyState,
        OperationSequenceGenerator sequenceGenerator,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(captureState);
        ArgumentNullException.ThrowIfNull(applyState);
        ArgumentNullException.ThrowIfNull(sequenceGenerator);

        return new StateCapturingOperationBuilder<TState>(
            captureState,
            applyState,
            sequenceGenerator,
            description);
    }
}

public sealed class StateCapturingOperationBuilder<TState>
{
    private readonly Func<TState> _captureState;
    private readonly Action<TState> _applyState;
    private readonly OperationSequenceGenerator _sequenceGenerator;
    private readonly string? _description;
    private readonly TState _beforeState;

    internal StateCapturingOperationBuilder(
        Func<TState> captureState,
        Action<TState> applyState,
        OperationSequenceGenerator sequenceGenerator,
        string? description)
    {
        _captureState = captureState;
        _applyState = applyState;
        _sequenceGenerator = sequenceGenerator;
        _description = description;
        _beforeState = captureState();
    }

    public CustomOperation Complete()
    {
        var afterState = _captureState();
        return CreateStateOperation(_beforeState, afterState);
    }

    private CustomOperation CreateStateOperation(TState fromState, TState toState)
    {
        return new CustomOperation(
            _ => _applyState(toState),
            _ => _applyState(fromState),
            _description) { SequenceNumber = _sequenceGenerator.GetNext() };
    }
}
