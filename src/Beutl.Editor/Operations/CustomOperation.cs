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

    /// <summary>Set only when both delegates leave state unchanged whenever they throw.</summary>
    public bool FailureIsAtomic { get; set; }

    public override ChangeOperationFailureState FailureState => FailureIsAtomic
        ? ChangeOperationFailureState.Unchanged : ChangeOperationFailureState.Unknown;

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
            description)
        { SequenceNumber = sequenceGenerator.GetNext() };
    }
}
