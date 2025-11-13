namespace Beutl.Protocol.Queries;

public class MutationContext
{
    public MutationContext(ICoreObject root)
    {
        Root = root;
    }

    public ICoreObject Root { get; }
}
