using Beutl.Collections;

namespace Beutl.UnitTests.Core;

[TestFixture]
public class HierarchicalListTests
{
    private sealed class TestNode : Hierarchical { }

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
    public void DefaultConstructor_AttachedDetachedNotInvokedAutomatically()
    {
        // 親なしコンストラクタはイベント購読を行わない
        var list = new HierarchicalList<IHierarchical>();
        var child = new TestNode();

        list.Add(child);
        list.Remove(child);

        // 親が居ないのでアタッチされていないこと
        Assert.That(((IHierarchical)child).HierarchicalParent, Is.Null);
    }
}
