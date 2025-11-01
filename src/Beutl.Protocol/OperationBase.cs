namespace Beutl.Protocol;

public abstract class OperationBase
{
    public required long SequenceNumber { get; set; }

    public abstract void Execute(OperationContext context);
}
