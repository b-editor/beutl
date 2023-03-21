namespace Beutl;

public readonly struct HierarchyAttachmentEventArgs
{
    public HierarchyAttachmentEventArgs(IHierarchicalRoot root, IHierarchical? parent)
    {
        Root = root;
        Parent = parent;
    }

    public IHierarchicalRoot Root { get; }

    public IHierarchical? Parent { get; }
}
