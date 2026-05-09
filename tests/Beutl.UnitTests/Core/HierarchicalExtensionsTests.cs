using Beutl.Collections;

namespace Beutl.UnitTests.Core;

public class HierarchicalExtensionsTests
{
    private class TestNode : Hierarchical, IModifiableHierarchical
    {
        public new ICoreList<IHierarchical> HierarchicalChildren => base.HierarchicalChildren;
    }

    private sealed class SpecialNode : TestNode;

    private sealed class TestRoot : Hierarchical, IHierarchicalRoot, IModifiableHierarchical
    {
        public new ICoreList<IHierarchical> HierarchicalChildren => base.HierarchicalChildren;

        public event EventHandler<IHierarchical>? DescendantAttached;
        public event EventHandler<IHierarchical>? DescendantDetached;

        public void OnDescendantAttached(IHierarchical descendant) => DescendantAttached?.Invoke(this, descendant);
        public void OnDescendantDetached(IHierarchical descendant) => DescendantDetached?.Invoke(this, descendant);
    }

    [Test]
    public void FindHierarchicalParent_Generic_FindsAncestor()
    {
        var root = new TestRoot();
        var middle = new TestNode();
        var leaf = new SpecialNode();
        root.HierarchicalChildren.Add(middle);
        middle.HierarchicalChildren.Add(leaf);

        TestRoot? found = ((IHierarchical)leaf).FindHierarchicalParent<TestRoot>();

        Assert.That(found, Is.SameAs(root));
    }

    [Test]
    public void FindHierarchicalParent_Generic_IncludeSelfMatchesSelf()
    {
        var leaf = new SpecialNode();

        SpecialNode? found = ((IHierarchical)leaf).FindHierarchicalParent<SpecialNode>(includeSelf: true);

        Assert.That(found, Is.SameAs(leaf));
    }

    [Test]
    public void FindHierarchicalParent_Generic_NoMatch_ReturnsDefault()
    {
        var root = new TestNode();

        TestRoot? found = ((IHierarchical)root).FindHierarchicalParent<TestRoot>();

        Assert.That(found, Is.Null);
    }

    [Test]
    public void FindRequiredHierarchicalParent_Generic_NoMatch_Throws()
    {
        var root = new TestNode();

        Assert.That(
            () => ((IHierarchical)root).FindRequiredHierarchicalParent<TestRoot>(),
            Throws.TypeOf<HierarchyException>());
    }

    [Test]
    public void FindRequiredHierarchicalParent_Generic_ReturnsAncestor()
    {
        var root = new TestRoot();
        var leaf = new TestNode();
        root.HierarchicalChildren.Add(leaf);

        TestRoot found = ((IHierarchical)leaf).FindRequiredHierarchicalParent<TestRoot>();

        Assert.That(found, Is.SameAs(root));
    }

    [Test]
    public void FindHierarchicalParent_Type_FindsAncestor()
    {
        var root = new TestRoot();
        var leaf = new TestNode();
        root.HierarchicalChildren.Add(leaf);

        IHierarchical? found = ((IHierarchical)leaf).FindHierarchicalParent(typeof(TestRoot));

        Assert.That(found, Is.SameAs(root));
    }

    [Test]
    public void FindHierarchicalParent_Type_IncludeSelf_ReturnsSelfWhenMatching()
    {
        var leaf = new TestNode();

        IHierarchical? found = ((IHierarchical)leaf).FindHierarchicalParent(typeof(TestNode), includeSelf: true);

        Assert.That(found, Is.SameAs(leaf));
    }

    [Test]
    public void FindRequiredHierarchicalParent_Type_NoMatch_Throws()
    {
        var root = new TestNode();

        Assert.That(
            () => ((IHierarchical)root).FindRequiredHierarchicalParent(typeof(TestRoot)),
            Throws.TypeOf<HierarchyException>());
    }

    [Test]
    public void FindHierarchicalRoot_FromLeaf_FindsRoot()
    {
        var root = new TestRoot();
        var leaf = new TestNode();
        root.HierarchicalChildren.Add(leaf);

        IHierarchicalRoot? found = ((IHierarchical)leaf).FindHierarchicalRoot();

        Assert.That(found, Is.SameAs(root));
    }

    [Test]
    public void FindHierarchicalRoot_NoRoot_ReturnsNull()
    {
        var node = new TestNode();

        IHierarchicalRoot? found = ((IHierarchical)node).FindHierarchicalRoot();

        Assert.That(found, Is.Null);
    }

    [Test]
    public void EnumerateAllChildren_VisitsDescendantsRecursively()
    {
        var root = new TestNode();
        var middle = new TestNode();
        var leaf1 = new SpecialNode();
        var leaf2 = new SpecialNode();
        root.HierarchicalChildren.Add(middle);
        middle.HierarchicalChildren.Add(leaf1);
        middle.HierarchicalChildren.Add(leaf2);

        SpecialNode[] specials = ((IHierarchical)root).EnumerateAllChildren<SpecialNode>().ToArray();

        Assert.That(specials, Is.EquivalentTo(new[] { leaf1, leaf2 }));
    }

    [Test]
    public void EnumerateAncestors_IteratesUpwardIncludingSelf()
    {
        var root = new TestRoot();
        var middle = new TestNode();
        var leaf = new SpecialNode();
        root.HierarchicalChildren.Add(middle);
        middle.HierarchicalChildren.Add(leaf);

        IHierarchical[] ancestors = ((IHierarchical)leaf).EnumerateAncestors<IHierarchical>().ToArray();

        Assert.That(ancestors[0], Is.SameAs(leaf));
        Assert.That(ancestors[^1], Is.SameAs(root));
    }
}
