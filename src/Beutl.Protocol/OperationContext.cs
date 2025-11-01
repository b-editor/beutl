namespace Beutl.Protocol;

public class OperationContext
{
    public OperationContext(CoreObject root)
    {
        Root = root;
    }

    public CoreObject Root { get; }

    public ICoreObject? FindObject(Guid id)
    {
        return Root.FindById(id);
    }
}
