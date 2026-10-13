using Beutl.Collections;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Core;

public class HierarchicalExtensionsTests
{
    private class TestNode : Hierarchical, IModifiableHierarchical
    {
        public new ICoreList<IHierarchical> HierarchicalChildren => base.HierarchicalChildren;
    }

    private sealed class SpecialNode : TestNode;

    [Test]
    public void FindHierarchicalParent_Generic_FindsAncestor()
    {
        var root = new TestHierarchicalRoot();
        var middle = new TestNode();
        var leaf = new SpecialNode();
        root.HierarchicalChildren.Add(middle);
        middle.HierarchicalChildren.Add(leaf);

        TestHierarchicalRoot? found = ((IHierarchical)leaf).FindHierarchicalParent<TestHierarchicalRoot>();

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

        TestHierarchicalRoot? found = ((IHierarchical)root).FindHierarchicalParent<TestHierarchicalRoot>();

        Assert.That(found, Is.Null);
    }

    [Test]
    public void FindRequiredHierarchicalParent_Generic_NoMatch_Throws()
    {
        var root = new TestNode();

        Assert.That(
            () => ((IHierarchical)root).FindRequiredHierarchicalParent<TestHierarchicalRoot>(),
            Throws.TypeOf<HierarchyException>());
    }

    [Test]
    public void FindRequiredHierarchicalParent_Generic_ReturnsAncestor()
    {
        var root = new TestHierarchicalRoot();
        var leaf = new TestNode();
        root.HierarchicalChildren.Add(leaf);

        TestHierarchicalRoot found = ((IHierarchical)leaf).FindRequiredHierarchicalParent<TestHierarchicalRoot>();

        Assert.That(found, Is.SameAs(root));
    }

    [Test]
    public void FindHierarchicalParent_Type_FindsAncestor()
    {
        var root = new TestHierarchicalRoot();
        var leaf = new TestNode();
        root.HierarchicalChildren.Add(leaf);

        IHierarchical? found = ((IHierarchical)leaf).FindHierarchicalParent(typeof(TestHierarchicalRoot));

        Assert.That(found, Is.SameAs(root));
        Assert.That(((IHierarchical)leaf).FindHierarchicalParent(typeof(IHierarchicalRoot)), Is.SameAs(root));
        Assert.That(((IHierarchical)leaf).FindHierarchicalParent(typeof(string)), Is.Null);
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
            () => ((IHierarchical)root).FindRequiredHierarchicalParent(typeof(TestHierarchicalRoot)),
            Throws.TypeOf<HierarchyException>());
    }

    [Test]
    public void FindHierarchicalRoot_FromLeaf_FindsRoot()
    {
        var root = new TestHierarchicalRoot();
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
        var root = new TestHierarchicalRoot();
        var middle = new TestNode();
        var leaf = new SpecialNode();
        root.HierarchicalChildren.Add(middle);
        middle.HierarchicalChildren.Add(leaf);

        IHierarchical[] ancestors = ((IHierarchical)leaf).EnumerateAncestors<IHierarchical>().ToArray();

        Assert.That(ancestors[0], Is.SameAs(leaf));
        Assert.That(ancestors[1], Is.SameAs(middle));
        Assert.That(ancestors[^1], Is.SameAs(root));
    }
}
