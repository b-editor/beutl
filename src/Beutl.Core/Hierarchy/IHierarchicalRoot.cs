namespace Beutl;

public interface IHierarchicalRoot : IHierarchical
{
    event EventHandler<IHierarchical> DescendantAttached;

    event EventHandler<IHierarchical> DescendantDetached;

    void OnDescendantAttached(IHierarchical descendant);

    void OnDescendantDetached(IHierarchical descendant);
}
