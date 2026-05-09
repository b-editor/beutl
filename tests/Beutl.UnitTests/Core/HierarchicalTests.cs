using Beutl.Collections;

namespace Beutl.UnitTests.Core;

[TestFixture]
public class HierarchicalTests
{
    private sealed class TestNode : Hierarchical, IModifiableHierarchical
    {
        public new ICoreList<IHierarchical> HierarchicalChildren => base.HierarchicalChildren;
    }

    private sealed class TestRoot : Hierarchical, IHierarchicalRoot, IModifiableHierarchical
    {
        public List<IHierarchical> AttachedDescendants { get; } = [];
        public List<IHierarchical> DetachedDescendants { get; } = [];

        public event EventHandler<IHierarchical>? DescendantAttached;
        public event EventHandler<IHierarchical>? DescendantDetached;

        public new ICoreList<IHierarchical> HierarchicalChildren => base.HierarchicalChildren;

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
        var root = new TestRoot();
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
        var root = new TestRoot();
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
        var root = new TestRoot();
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
        var root = new TestRoot();
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
        var root = new TestRoot();
        IHierarchical parent = root;
        var args = new HierarchyAttachmentEventArgs(root, parent);

        Assert.That(args.Root, Is.SameAs(root));
        Assert.That(args.Parent, Is.SameAs(parent));
    }

    [Test]
    public void HierarchyException_DefaultCtor()
    {
        var ex = new HierarchyException();
        Assert.That(ex.Message, Is.Not.Null);
    }

    [Test]
    public void HierarchyException_MessageCtor()
    {
        var ex = new HierarchyException("oops");
        Assert.That(ex.Message, Is.EqualTo("oops"));
    }

    [Test]
    public void HierarchyException_InnerCtor()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new HierarchyException("outer", inner);
        Assert.That(ex.InnerException, Is.SameAs(inner));
    }

    [Test]
    public void FindHierarchicalParent_Generic_FindsAncestor()
    {
        var root = new TestRoot();
        var middle = new TestNode();
        var leaf = new TestNode();
        middle.HierarchicalChildren.Add(leaf);
        root.HierarchicalChildren.Add(middle);

        Assert.That(leaf.FindHierarchicalParent<TestRoot>(), Is.SameAs(root));
        Assert.That(leaf.FindHierarchicalParent<TestNode>(), Is.SameAs(middle));
    }

    [Test]
    public void FindHierarchicalParent_IncludeSelf_ReturnsSelf()
    {
        var node = new TestNode();
        Assert.That(node.FindHierarchicalParent<TestNode>(includeSelf: true), Is.SameAs(node));
    }

    [Test]
    public void FindHierarchicalParent_NotFound_ReturnsDefault()
    {
        var node = new TestNode();
        Assert.That(node.FindHierarchicalParent<TestRoot>(), Is.Null);
    }

    [Test]
    public void FindRequiredHierarchicalParent_NotFound_Throws()
    {
        var node = new TestNode();
        Assert.Throws<HierarchyException>(() => node.FindRequiredHierarchicalParent<TestRoot>());
    }

    [Test]
    public void FindRequiredHierarchicalParent_Found_Returns()
    {
        var root = new TestRoot();
        var leaf = new TestNode();
        root.HierarchicalChildren.Add(leaf);

        Assert.That(leaf.FindRequiredHierarchicalParent<TestRoot>(), Is.SameAs(root));
    }

    [Test]
    public void FindHierarchicalParent_TypeOverload_Works()
    {
        var root = new TestRoot();
        var leaf = new TestNode();
        root.HierarchicalChildren.Add(leaf);

        Assert.That(leaf.FindHierarchicalParent(typeof(TestRoot)), Is.SameAs(root));
        Assert.That(leaf.FindHierarchicalParent(typeof(IHierarchicalRoot)), Is.SameAs(root));
        Assert.That(leaf.FindHierarchicalParent(typeof(string)), Is.Null);
    }

    [Test]
    public void FindRequiredHierarchicalParent_TypeOverload_Throws()
    {
        var node = new TestNode();
        Assert.Throws<HierarchyException>(() => node.FindRequiredHierarchicalParent(typeof(TestRoot)));
    }

    [Test]
    public void FindHierarchicalRoot_ReturnsRoot()
    {
        var root = new TestRoot();
        var leaf = new TestNode();
        root.HierarchicalChildren.Add(leaf);

        Assert.That(leaf.FindHierarchicalRoot(), Is.SameAs(root));
    }

    [Test]
    public void FindHierarchicalRoot_OrphanReturnsNull()
    {
        var node = new TestNode();
        Assert.That(node.FindHierarchicalRoot(), Is.Null);
    }

    [Test]
    public void EnumerateAllChildren_ReturnsDescendants()
    {
        var root = new TestNode();
        var middle = new TestNode();
        var leaf1 = new TestNode();
        var leaf2 = new TestNode();
        middle.HierarchicalChildren.Add(leaf1);
        middle.HierarchicalChildren.Add(leaf2);
        root.HierarchicalChildren.Add(middle);

        var results = ((IHierarchical)root).EnumerateAllChildren<TestNode>().ToList();

        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results, Does.Contain(middle));
        Assert.That(results, Does.Contain(leaf1));
        Assert.That(results, Does.Contain(leaf2));
    }

    [Test]
    public void EnumerateAncestors_StartsFromSelf()
    {
        var root = new TestRoot();
        var middle = new TestNode();
        var leaf = new TestNode();
        middle.HierarchicalChildren.Add(leaf);
        root.HierarchicalChildren.Add(middle);

        var ancestors = ((IHierarchical)leaf).EnumerateAncestors<IHierarchical>().ToList();

        Assert.That(ancestors[0], Is.SameAs(leaf));
        Assert.That(ancestors[1], Is.SameAs(middle));
        Assert.That(ancestors[2], Is.SameAs(root));
    }
}
