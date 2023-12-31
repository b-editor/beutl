namespace Beutl;

public readonly struct HierarchyAttachmentEventArgs(IHierarchicalRoot root, IHierarchical? parent)
{
    public IHierarchicalRoot Root { get; } = root;

    public IHierarchical? Parent { get; } = parent;
}
