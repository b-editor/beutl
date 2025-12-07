namespace Beutl.Editor.Operations;

public sealed class CustomOperation : ChangeOperation
{
    private readonly Action<OperationExecutionContext> _apply;
    private readonly Func<OperationExecutionContext, OperationSequenceGenerator, ChangeOperation> _createRevert;

    public CustomOperation(
        Action<OperationExecutionContext> apply,
        Func<OperationExecutionContext, OperationSequenceGenerator, ChangeOperation> createRevert,
        string? description = null)
    {
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _createRevert = createRevert ?? throw new ArgumentNullException(nameof(createRevert));
        Description = description;
    }

    public string? Description { get; }

    public override void Apply(OperationExecutionContext context)
    {
        _apply(context);
    }

    public override ChangeOperation CreateRevertOperation(
        OperationExecutionContext context,
        OperationSequenceGenerator sequenceGenerator)
    {
        return _createRevert(context, sequenceGenerator);
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
            (_, seq) => new CustomOperation(
                _ => undoAction(),
                (_, seq2) => Create(doAction, undoAction, seq2, description),
                description)
            {
                SequenceNumber = seq.GetNext()
            },
            description)
        {
            SequenceNumber = sequenceGenerator.GetNext()
        };
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
            (_, seq) =>
            {
                var innerOp = new CustomOperation(
                    _ => _applyState(fromState),
                    (_, seq2) =>
                    {
                        var op = CreateStateOperation(fromState, toState);
                        op.SequenceNumber = seq2.GetNext();
                        return op;
                    },
                    _description)
                {
                    SequenceNumber = seq.GetNext()
                };
                return innerOp;
            },
            _description)
        {
            SequenceNumber = _sequenceGenerator.GetNext()
        };
    }
}
