namespace Beutl.Editor.Operations;

public abstract class ChangeOperation
{
    public required long SequenceNumber { get; set; }

    public abstract void Apply(OperationExecutionContext context);

    public abstract void Revert(OperationExecutionContext context);
}
