using Beutl.Collections;

namespace Beutl.UnitTests.Core;

[TestFixture]
public class HierarchicalListTests
{
    private sealed class TestNode : Hierarchical
    {
    }

    [Test]
    public void DefaultConstructor_HasNoParentAndRemovesOnReset()
    {
        var list = new HierarchicalList<IHierarchical>();
        Assert.Multiple(() =>
        {
            Assert.That(list.Parent, Is.Null);
            Assert.That(list.ResetBehavior, Is.EqualTo(ResetBehavior.Remove));
        });
    }

    [Test]
    public void ParentConstructor_StoresParent()
    {
        var parent = new TestNode();
        var list = new HierarchicalList<IHierarchical>(parent);
        Assert.That(list.Parent, Is.SameAs(parent));
    }

    [Test]
    public void Add_AttachesChildToParent()
    {
        var parent = new TestNode();
        var list = new HierarchicalList<IHierarchical>(parent);
        var child = new TestNode();

        list.Add(child);

        Assert.That(((IHierarchical)child).HierarchicalParent, Is.SameAs(parent));
    }

    [Test]
    public void Remove_DetachesChildFromParent()
    {
        var parent = new TestNode();
        var list = new HierarchicalList<IHierarchical>(parent);
        var child = new TestNode();
        list.Add(child);

        list.Remove(child);

        Assert.That(((IHierarchical)child).HierarchicalParent, Is.Null);
    }

    [Test]
    public void Clear_DetachesAllChildren()
    {
        var parent = new TestNode();
        var list = new HierarchicalList<IHierarchical>(parent);
        var c1 = new TestNode();
        var c2 = new TestNode();
        list.Add(c1);
        list.Add(c2);

        list.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(((IHierarchical)c1).HierarchicalParent, Is.Null);
            Assert.That(((IHierarchical)c2).HierarchicalParent, Is.Null);
            Assert.That(list.Count, Is.EqualTo(0));
        });
    }

    [Test]
    public void Replace_DetachesOldAndAttachesNew()
    {
        var parent = new TestNode();
        var list = new HierarchicalList<IHierarchical>(parent);
        var oldChild = new TestNode();
        var newChild = new TestNode();
        list.Add(oldChild);

        list[0] = newChild;

        Assert.Multiple(() =>
        {
            Assert.That(((IHierarchical)oldChild).HierarchicalParent, Is.Null);
            Assert.That(((IHierarchical)newChild).HierarchicalParent, Is.SameAs(parent));
        });
    }

    [Test]
    public void ReplaceAll_KeepsParentOfChildrenInBothLists()
    {
        var parent = new TestNode();
        var list = new HierarchicalList<IHierarchical>(parent);
        var removedChild = new TestNode();
        var keptChild = new TestNode();
        var addedChild = new TestNode();
        list.AddRange(new IHierarchical[] { removedChild, keptChild });

        list.Replace([keptChild, addedChild]);

        Assert.Multiple(() =>
        {
            Assert.That(list, Is.EqualTo(new IHierarchical[] { keptChild, addedChild }));
            Assert.That(((IHierarchical)removedChild).HierarchicalParent, Is.Null);
            Assert.That(((IHierarchical)keptChild).HierarchicalParent, Is.SameAs(parent));
            Assert.That(((IHierarchical)addedChild).HierarchicalParent, Is.SameAs(parent));
            Assert.That(
                ((IHierarchical)parent).HierarchicalChildren,
                Is.EquivalentTo(new IHierarchical[] { keptChild, addedChild }));
        });
    }

    [Test]
    public void DefaultConstructor_AttachedDetachedNotInvokedAutomatically()
    {
        var list = new HierarchicalList<IHierarchical>();
        var child = new TestNode();

        list.Add(child);

        Assert.That(((IHierarchical)child).HierarchicalParent, Is.Null);
    }

    [Test]
    public void InsertingTheParentOrItsAncestor_ThrowsAndLeavesTheListUnchanged()
    {
        var root = new TestNode();
        var parent = new TestNode();
        new HierarchicalList<IHierarchical>(root).Add(parent);
        var list = new HierarchicalList<IHierarchical>(parent);
        var child = new TestNode();
        list.Add(child);

        foreach (IHierarchical item in new IHierarchical[] { parent, root })
        {
            Assert.Throws<InvalidOperationException>(() => list.Add(item));
            Assert.Throws<InvalidOperationException>(() => list.Insert(0, item));
            Assert.Throws<InvalidOperationException>(() => list.InsertRange(0, new List<IHierarchical> { item }));
            Assert.Throws<InvalidOperationException>(() => list.InsertRange(0, Enumerate(item)));
            Assert.Throws<InvalidOperationException>(() => list.InsertRange(0, new[] { item }));
            Assert.Throws<InvalidOperationException>(() => list.InsertRange(0, (ReadOnlySpan<IHierarchical>)[item]));
            Assert.Throws<InvalidOperationException>(() => list.Replace([item]));
            Assert.Throws<InvalidOperationException>(() => list[0] = item);
        }

        Assert.Multiple(() =>
        {
            Assert.That(list, Is.EqualTo(new IHierarchical[] { child }));
            Assert.That(((IHierarchical)parent).HierarchicalParent, Is.SameAs(root));
            Assert.That(((IHierarchical)root).HierarchicalParent, Is.Null);
        });

        static IEnumerable<IHierarchical> Enumerate(IHierarchical item)
        {
            yield return item;
        }
    }
}
