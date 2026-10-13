using Beutl.Collections;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Core;

[TestFixture]
public class HierarchicalTests
{
    private sealed class TestNode : Hierarchical, IModifiableHierarchical
    {
        public new ICoreList<IHierarchical> HierarchicalChildren => base.HierarchicalChildren;
    }

    [Test]
    public void AddChild_SetsParent()
    {
        var parent = new TestNode();
        var child = new TestNode();

        parent.HierarchicalChildren.Add(child);

        Assert.That(((IHierarchical)child).HierarchicalParent, Is.SameAs(parent));
    }

    [Test]
    public void RemoveChild_ClearsParent()
    {
        var parent = new TestNode();
        var child = new TestNode();

        parent.HierarchicalChildren.Add(child);
        parent.HierarchicalChildren.Remove(child);

        Assert.That(((IHierarchical)child).HierarchicalParent, Is.Null);
    }

    [Test]
    public void AttachToRoot_RaisesAttachedEvents()
    {
        var root = new TestHierarchicalRoot();
        var child = new TestNode();
        var attachedFired = false;
        child.AttachedToHierarchy += (_, _) => attachedFired = true;

        root.HierarchicalChildren.Add(child);

        Assert.That(attachedFired, Is.True);
        Assert.That(root.AttachedDescendants, Does.Contain(child));
        Assert.That(((IHierarchical)child).HierarchicalRoot, Is.SameAs(root));
    }

    [Test]
    public void DetachFromRoot_RaisesDetachedEvents()
    {
        var root = new TestHierarchicalRoot();
        var child = new TestNode();
        root.HierarchicalChildren.Add(child);

        var detachedFired = false;
        child.DetachedFromHierarchy += (_, _) => detachedFired = true;

        root.HierarchicalChildren.Remove(child);

        Assert.That(detachedFired, Is.True);
        Assert.That(root.DetachedDescendants, Does.Contain(child));
        Assert.That(((IHierarchical)child).HierarchicalRoot, Is.Null);
    }

    [Test]
    public void NestedAttach_PropagatesToDescendants()
    {
        var root = new TestHierarchicalRoot();
        var middle = new TestNode();
        var leaf = new TestNode();
        middle.HierarchicalChildren.Add(leaf);

        root.HierarchicalChildren.Add(middle);

        Assert.That(((IHierarchical)leaf).HierarchicalRoot, Is.SameAs(root));
        Assert.That(root.AttachedDescendants, Does.Contain(leaf));
    }

    [Test]
    public void NestedDetach_PropagatesToDescendants()
    {
        var root = new TestHierarchicalRoot();
        var middle = new TestNode();
        var leaf = new TestNode();
        middle.HierarchicalChildren.Add(leaf);
        root.HierarchicalChildren.Add(middle);

        root.HierarchicalChildren.Remove(middle);

        Assert.That(((IHierarchical)leaf).HierarchicalRoot, Is.Null);
        Assert.That(root.DetachedDescendants, Does.Contain(leaf));
    }

    [Test]
    public void DoubleParent_ThrowsInvalidOperation()
    {
        var parent1 = new TestNode();
        var parent2 = new TestNode();
        var child = new TestNode();
        parent1.HierarchicalChildren.Add(child);

        Assert.Throws<InvalidOperationException>(() =>
            ((IModifiableHierarchical)child).SetParent(parent2));
    }

    [Test]
    public void HierarchyAttachmentEventArgs_StoresValues()
    {
        var root = new TestHierarchicalRoot();
        IHierarchical parent = root;
        var args = new HierarchyAttachmentEventArgs(root, parent);

        Assert.That(args.Root, Is.SameAs(root));
        Assert.That(args.Parent, Is.SameAs(parent));
    }
}
