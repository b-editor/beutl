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
