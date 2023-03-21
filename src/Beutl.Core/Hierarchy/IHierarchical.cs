using Beutl.Collections;

namespace Beutl;

public interface IHierarchical
{
    IHierarchical? HierarchicalParent { get; }

    IHierarchicalRoot? HierarchicalRoot { get; }

    ICoreReadOnlyList<IHierarchical> HierarchicalChildren { get; }

    event EventHandler<HierarchyAttachmentEventArgs> AttachedToHierarchy;
    event EventHandler<HierarchyAttachmentEventArgs> DetachedFromHierarchy;
}
