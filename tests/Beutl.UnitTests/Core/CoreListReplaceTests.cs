using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Beutl.Collections;
using DynamicData;
using DynamicData.Binding;

namespace Beutl.UnitTests.Core;

// CoreList<T>.Replace swaps the whole list and notifies the swap the way Clear does: one Reset, or a Remove
// of the old items followed by an Add of the new ones, depending on ResetBehavior. It must never raise a
// Replace whose old and new counts differ: consumers such as DynamicData apply only NewItems[0] of a
// Replace (and throw when NewItems is empty).
[TestFixture]
public class CoreListReplaceTests
{
    private static readonly (string Name, string[] Initial, string[] Replacement)[] s_replacements =
    [
        ("with an empty list", ["a", "b"], []),
        ("with a shorter list", ["a", "b", "c"], ["x"]),
        ("with a longer list", ["a"], ["x", "y", "z"]),
        ("with a list of the same length", ["a", "b"], ["x", "y"]),
        ("with some of the same items", ["a", "b", "c"], ["c", "a"]),
        ("on an empty list", [], ["x", "y"]),
    ];

    private static IEnumerable<TestCaseData> ReplaceCases()
        => from behavior in new[] { ResetBehavior.Reset, ResetBehavior.Remove }
           from replacement in s_replacements
           select new TestCaseData(behavior, replacement.Initial, replacement.Replacement)
               .SetArgDisplayNames(behavior.ToString(), replacement.Name);

    private static CoreList<string> CreateList(ResetBehavior behavior, params string[] items)
        => new(items) { ResetBehavior = behavior };

    private static List<NotifyCollectionChangedEventArgs> RecordChanges<T>(CoreList<T> list)
    {
        var changes = new List<NotifyCollectionChangedEventArgs>();
        list.CollectionChanged += (_, e) => changes.Add(e);
        return changes;
    }

    [Test]
    public void Replace_OnResetList_RaisesOneResetAfterSwapping()
    {
        CoreList<string> list = CreateList(ResetBehavior.Reset, "a", "b");
        var actions = new List<NotifyCollectionChangedAction>();
        var snapshots = new List<string[]>();
        list.CollectionChanged += (_, e) =>
        {
            actions.Add(e.Action);
            snapshots.Add([.. list]);
        };
        var detached = new List<string>();
        var attached = new List<string>();
        var properties = new List<string?>();
        list.Detached += detached.Add;
        list.Attached += attached.Add;
        list.PropertyChanged += (_, e) => properties.Add(e.PropertyName);

        list.Replace(["x"]);

        Assert.Multiple(() =>
        {
            Assert.That(list, Is.EqualTo(new[] { "x" }));
            Assert.That(actions, Is.EqualTo(new[] { NotifyCollectionChangedAction.Reset }));
            Assert.That(snapshots, Is.EqualTo(new[] { new[] { "x" } }));
            Assert.That(detached, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(attached, Is.EqualTo(new[] { "x" }));
            Assert.That(properties, Does.Contain(nameof(CoreList<string>.Count)));
        });
    }

    [Test]
    public void Replace_OnRemoveList_WithEmptyList_RaisesOnlyRemove()
    {
        CoreList<string> list = CreateList(ResetBehavior.Remove, "a", "b");
        List<NotifyCollectionChangedEventArgs> changes = RecordChanges(list);
        var detached = new List<string>();
        var attached = new List<string>();
        var properties = new List<string?>();
        list.Detached += detached.Add;
        list.Attached += attached.Add;
        list.PropertyChanged += (_, e) => properties.Add(e.PropertyName);

        list.Replace([]);

        Assert.Multiple(() =>
        {
            Assert.That(list, Is.Empty);
            Assert.That(changes, Has.Count.EqualTo(1));
            Assert.That(changes[0].Action, Is.EqualTo(NotifyCollectionChangedAction.Remove));
            Assert.That(changes[0].OldStartingIndex, Is.Zero);
            Assert.That(changes[0].OldItems, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(detached, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(attached, Is.Empty);
            Assert.That(properties, Does.Contain(nameof(CoreList<string>.Count)));
        });
    }

    [Test]
    public void Replace_OnRemoveList_WithShorterList_RaisesRemoveThenAdd()
    {
        CoreList<string> list = CreateList(ResetBehavior.Remove, "a", "b", "c");
        List<NotifyCollectionChangedEventArgs> changes = RecordChanges(list);

        list.Replace(["x"]);

        Assert.Multiple(() =>
        {
            Assert.That(list, Is.EqualTo(new[] { "x" }));
            Assert.That(changes.Select(e => e.Action), Is.EqualTo(new[]
            {
                NotifyCollectionChangedAction.Remove,
                NotifyCollectionChangedAction.Add
            }));
            Assert.That(changes[0].OldStartingIndex, Is.Zero);
            Assert.That(changes[0].OldItems, Is.EqualTo(new[] { "a", "b", "c" }));
            Assert.That(changes[1].NewStartingIndex, Is.Zero);
            Assert.That(changes[1].NewItems, Is.EqualTo(new[] { "x" }));
        });
    }

    [Test]
    public void Replace_OnRemoveList_WithLongerList_RaisesRemoveThenAdd()
    {
        CoreList<string> list = CreateList(ResetBehavior.Remove, "a");
        List<NotifyCollectionChangedEventArgs> changes = RecordChanges(list);
        var detached = new List<string>();
        var attached = new List<string>();
        list.Detached += detached.Add;
        list.Attached += attached.Add;

        list.Replace(["x", "y", "z"]);

        Assert.Multiple(() =>
        {
            Assert.That(list, Is.EqualTo(new[] { "x", "y", "z" }));
            Assert.That(changes.Select(e => e.Action), Is.EqualTo(new[]
            {
                NotifyCollectionChangedAction.Remove,
                NotifyCollectionChangedAction.Add
            }));
            Assert.That(changes[0].OldStartingIndex, Is.Zero);
            Assert.That(changes[0].OldItems, Is.EqualTo(new[] { "a" }));
            Assert.That(changes[1].NewStartingIndex, Is.Zero);
            Assert.That(changes[1].NewItems, Is.EqualTo(new[] { "x", "y", "z" }));
            Assert.That(detached, Is.EqualTo(new[] { "a" }));
            Assert.That(attached, Is.EqualTo(new[] { "x", "y", "z" }));
        });
    }

    [Test]
    public void Replace_OnEmptyRemoveList_RaisesOnlyAdd()
    {
        CoreList<string> list = CreateList(ResetBehavior.Remove);
        List<NotifyCollectionChangedEventArgs> changes = RecordChanges(list);

        list.Replace(["x", "y"]);

        Assert.Multiple(() =>
        {
            Assert.That(list, Is.EqualTo(new[] { "x", "y" }));
            Assert.That(changes, Has.Count.EqualTo(1));
            Assert.That(changes[0].Action, Is.EqualTo(NotifyCollectionChangedAction.Add));
            Assert.That(changes[0].NewStartingIndex, Is.Zero);
            Assert.That(changes[0].NewItems, Is.EqualTo(new[] { "x", "y" }));
        });
    }

    [Test]
    public void Replace_OnRemoveList_ListMatchesEachNotification()
    {
        CoreList<string> list = CreateList(ResetBehavior.Remove, "a", "b");
        var snapshots = new List<string[]>();
        list.CollectionChanged += (_, _) => snapshots.Add([.. list]);

        list.Replace(["x", "y", "z"]);

        Assert.That(snapshots, Is.EqualTo(new[]
        {
            Array.Empty<string>(),
            new[] { "x", "y", "z" }
        }));
    }

    [Test]
    public void Replace_OnRemoveList_UsesTheItemsGivenWhenCalled()
    {
        CoreList<string> list = CreateList(ResetBehavior.Remove, "a");
        var source = new List<string> { "x", "y" };
        list.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Remove)
            {
                source.Clear();
            }
        };

        list.Replace(source);

        Assert.That(list, Is.EqualTo(new[] { "x", "y" }));
    }

    [Test]
    public void Replace_OnRemoveList_WhenRemoveHandlerThrows_StillAddsTheNewItems()
    {
        CoreList<string> list = CreateList(ResetBehavior.Remove, "a", "b");
        var attached = new List<string>();
        var added = new List<string>();
        list.Attached += attached.Add;
        list.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Remove)
            {
                throw new InvalidOperationException("Remove handler failed.");
            }

            added.AddRange(e.NewItems!.Cast<string>());
        };

        Assert.Throws<InvalidOperationException>(() => list.Replace(["x", "y"]));
        Assert.Multiple(() =>
        {
            Assert.That(list, Is.EqualTo(new[] { "x", "y" }));
            Assert.That(attached, Is.EqualTo(new[] { "x", "y" }));
            Assert.That(added, Is.EqualTo(new[] { "x", "y" }));
        });
    }

    [TestCase(ResetBehavior.Reset)]
    [TestCase(ResetBehavior.Remove)]
    public void Replace_WithEqualItems_RaisesNothing(ResetBehavior behavior)
    {
        CoreList<string> list = CreateList(behavior, "a", "b");
        List<NotifyCollectionChangedEventArgs> changes = RecordChanges(list);
        var properties = new List<string?>();
        list.PropertyChanged += (_, e) => properties.Add(e.PropertyName);

        list.Replace(["a", "b"]);

        Assert.Multiple(() =>
        {
            Assert.That(changes, Is.Empty);
            Assert.That(properties, Is.Empty);
        });
    }

    [Test]
    public void Replace_WithNull_Throws()
    {
        var list = new CoreList<string>("a");

        Assert.Throws<ArgumentNullException>(() => list.Replace(null!));
        Assert.That(list, Is.EqualTo(new[] { "a" }));
    }

    [TestCaseSource(nameof(ReplaceCases))]
    public void Replace_KeepsToObservableChangeSetSubscriberInSync(
        ResetBehavior behavior, string[] initial, string[] replacement)
    {
        CoreList<string> list = CreateList(behavior, initial);
        using IDisposable subscription = list.ToObservableChangeSet<CoreList<string>, string>()
            .Bind(out ReadOnlyObservableCollection<string> mirror)
            .Subscribe();

        Assert.DoesNotThrow(() => list.Replace(replacement));

        Assert.That(mirror, Is.EqualTo(replacement));
    }

    [TestCaseSource(nameof(ReplaceCases))]
    public void Replace_KeepsForEachItemSubscriberInSync(
        ResetBehavior behavior, string[] initial, string[] replacement)
    {
        CoreList<string> list = CreateList(behavior, initial);
        var mirror = new List<string>();
        using IDisposable subscription = list.ForEachItem<string>(
            (index, item) => mirror.Insert(index, item),
            (index, item) =>
            {
                Assert.That(mirror[index], Is.EqualTo(item));
                mirror.RemoveAt(index);
            },
            mirror.Clear);

        list.Replace(replacement);

        Assert.That(mirror, Is.EqualTo(replacement));
    }
}
