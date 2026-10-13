using Beutl.Collections;

namespace Beutl.UnitTests.TestInfrastructure;

// A plain hierarchy root to attach test objects under, so that FindHierarchicalRoot() succeeds, as
// reference-expression resolution needs; a detached object has no root. It is not an EngineObject,
// because an EngineObject root would hijack an owner's time-anchor subscription. It records the
// descendants it is told about.
public sealed class TestHierarchicalRoot : Hierarchical, IHierarchicalRoot
{
    public new ICoreList<IHierarchical> HierarchicalChildren => base.HierarchicalChildren;

    public List<IHierarchical> AttachedDescendants { get; } = [];

    public List<IHierarchical> DetachedDescendants { get; } = [];

    public event EventHandler<IHierarchical>? DescendantAttached;

    public event EventHandler<IHierarchical>? DescendantDetached;

    public void OnDescendantAttached(IHierarchical descendant)
    {
        AttachedDescendants.Add(descendant);
        DescendantAttached?.Invoke(this, descendant);
    }

    public void OnDescendantDetached(IHierarchical descendant)
    {
        DetachedDescendants.Add(descendant);
        DescendantDetached?.Invoke(this, descendant);
    }
}
