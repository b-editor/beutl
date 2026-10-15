namespace Beutl.Editor.Operations;

public sealed class CustomOperation : ChangeOperation
{
    private readonly Action _apply;
    private readonly Action _revert;

    public CustomOperation(Action apply, Action revert)
    {
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _revert = revert ?? throw new ArgumentNullException(nameof(revert));
    }

    /// <summary>Set only when both delegates leave state unchanged whenever they throw.</summary>
    public bool FailureIsAtomic { get; set; }

    public override ChangeOperationFailureState FailureState => FailureIsAtomic
        ? ChangeOperationFailureState.Unchanged : ChangeOperationFailureState.Unknown;

    public override void Apply()
    {
        _apply();
    }

    public override void Revert()
    {
        _revert();
    }

    public static CustomOperation Create(
        Action doAction,
        Action undoAction,
        OperationSequenceGenerator sequenceGenerator)
    {
        ArgumentNullException.ThrowIfNull(doAction);
        ArgumentNullException.ThrowIfNull(undoAction);
        ArgumentNullException.ThrowIfNull(sequenceGenerator);

        return new CustomOperation(doAction, undoAction)
        { SequenceNumber = sequenceGenerator.GetNext() };
    }
}
