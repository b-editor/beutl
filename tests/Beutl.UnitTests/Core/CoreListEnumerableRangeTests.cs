using System.Collections;
using Beutl.Collections;

namespace Beutl.UnitTests.Core;

[TestFixture]
public class CoreListEnumerableRangeTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void EnumerableRange_NotifiesWithoutCollectionChangedSubscribers(bool insert)
    {
        var list = new CoreList<int>([1, 4]);
        var attached = new List<int>();
        var properties = new List<string?>();
        list.Attached += attached.Add;
        list.PropertyChanged += (_, e) => properties.Add(e.PropertyName);

        if (insert)
            list.InsertRange(1, Enumerate(2, 3));
        else
            list.AddRange(Enumerate(2, 3));

        Assert.Multiple(() =>
        {
            Assert.That(list, Is.EqualTo(insert ? new[] { 1, 2, 3, 4 } : [1, 4, 2, 3]));
            Assert.That(attached, Is.EqualTo(new[] { 2, 3 }));
            Assert.That(properties, Is.EqualTo(new[] { "Item[]", nameof(list.Count) }));
        });
    }

    [Test]
    public void EnumerableRange_AttachesHierarchicalChildren()
    {
        var parent = new TestNode();
        var list = new HierarchicalList<TestNode>(parent);
        var first = new TestNode();
        var second = new TestNode();

        list.AddRange(Enumerate(first, second));

        Assert.Multiple(() =>
        {
            Assert.That(first.HierarchicalParent, Is.SameAs(parent));
            Assert.That(second.HierarchicalParent, Is.SameAs(parent));
            Assert.That(((IHierarchical)parent).HierarchicalChildren, Is.EqualTo(new[] { first, second }));
        });

        list.Clear();
        Assert.Multiple(() =>
        {
            Assert.That(first.HierarchicalParent, Is.Null);
            Assert.That(second.HierarchicalParent, Is.Null);
            Assert.That(((IHierarchical)parent).HierarchicalChildren, Is.Empty);
        });
    }

    [Test]
    public void EnumerableRange_WithOnlyPropertyChangedSubscriber_NotifiesCountAndIndexer()
    {
        var list = new CoreList<int>();
        var properties = new List<string?>();
        list.PropertyChanged += (_, e) => properties.Add(e.PropertyName);

        list.AddRange(Enumerate(2, 3));

        Assert.That(properties, Is.EqualTo(new[] { "Item[]", nameof(list.Count) }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ThrowingEnumerableRange_NotifiesAndAttachesInsertedChildren(bool implementsList)
    {
        var parent = new TestNode();
        var list = new HierarchicalList<TestNode>(parent);
        var child = new TestNode();
        var notifications = new List<TestNode>();
        list.CollectionChanged += (_, e) => notifications.AddRange(e.NewItems!.Cast<TestNode>());

        IEnumerable<TestNode> items = implementsList
            ? new ListEnumerable<TestNode>([child], EnumerateAndThrow(child))
            : EnumerateAndThrow(child);
        Assert.Throws<InvalidOperationException>(() => list.AddRange(items));

        Assert.Multiple(() =>
        {
            Assert.That(list, Is.EqualTo(new[] { child }));
            Assert.That(child.HierarchicalParent, Is.SameAs(parent));
            Assert.That(((IHierarchical)parent).HierarchicalChildren, Is.EqualTo(new[] { child }));
            Assert.That(notifications, Is.EqualTo(new[] { child }));
        });
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void EnumerableRange_WhenHandlersThrow_NotifiesEveryObserverAndPreservesFirstFailure(
        bool implementsList, bool enumerationFails)
    {
        var list = new CoreList<int>([1, 4]);
        var enumerationFailure = new InvalidOperationException("Enumeration failed.");
        var notificationFailure = new InvalidOperationException("Attachment failed.");
        var attached = new List<int>();
        var properties = new List<string?>();
        var added = new List<int>();
        int addedIndex = -1;
        list.Attached += _ => throw notificationFailure;
        list.Attached += attached.Add;
        list.PropertyChanged += (_, _) => throw new InvalidOperationException("Property notification failed.");
        list.PropertyChanged += (_, e) => properties.Add(e.PropertyName);
        list.CollectionChanged += (_, _) => throw new InvalidOperationException("Collection notification failed.");
        list.CollectionChanged += (_, e) =>
        {
            added.AddRange(e.NewItems!.Cast<int>());
            addedIndex = e.NewStartingIndex;
        };

        IEnumerable<int> source = EnumerateWithOptionalFailure();
        if (implementsList)
            source = new ListEnumerable<int>([2, 3], source);

        Exception? failure = Assert.Throws<InvalidOperationException>(() => list.InsertRange(1, source));

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.SameAs(enumerationFails ? enumerationFailure : notificationFailure));
            if (enumerationFails)
                Assert.That(failure!.StackTrace, Does.Contain(nameof(EnumerateWithOptionalFailure)));
            Assert.That(list, Is.EqualTo(new[] { 1, 2, 3, 4 }));
            Assert.That(attached, Is.EqualTo(new[] { 2, 3 }));
            Assert.That(properties, Is.EqualTo(new[] { "Item[]", nameof(list.Count) }));
            Assert.That(added, Is.EqualTo(new[] { 2, 3 }));
            Assert.That(addedIndex, Is.EqualTo(1));
        });

        IEnumerable<int> EnumerateWithOptionalFailure()
        {
            yield return 2;
            yield return 3;
            if (enumerationFails)
                throw enumerationFailure;
        }
    }

    private static IEnumerable<T> Enumerate<T>(T first, T second)
    {
        yield return first;
        yield return second;
    }

    private static IEnumerable<T> EnumerateAndThrow<T>(T item)
    {
        yield return item;
        throw new InvalidOperationException("Enumeration failed after inserting an item.");
    }

    private sealed class TestNode : Hierarchical;

    private sealed class ListEnumerable<T>(T[] items, IEnumerable<T> enumerable) : ArrayList(items), IEnumerable<T>
    {
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => enumerable.GetEnumerator();
    }
}
