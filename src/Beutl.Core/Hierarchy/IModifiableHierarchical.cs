namespace Beutl;

public interface IModifiableHierarchical : IHierarchical
{
    void AddChild(IHierarchical child);

    void RemoveChild(IHierarchical child);

    void SetParent(IHierarchical? parent);

    void NotifyAttachedToHierarchy(in HierarchyAttachmentEventArgs e);

    void NotifyDetachedFromHierarchy(in HierarchyAttachmentEventArgs e);
}
