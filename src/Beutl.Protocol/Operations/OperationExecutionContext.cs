namespace Beutl.Protocol.Operations;

public sealed class OperationExecutionContext
{
    public OperationExecutionContext(CoreObject root)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
    }

    public CoreObject Root { get; }

    public ICoreObject? FindObject(Guid id)
    {
        return Root.FindById(id);
    }
}
