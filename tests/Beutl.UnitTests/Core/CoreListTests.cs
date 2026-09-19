using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Beutl.Collections;
using DynamicData;
using DynamicData.Binding;

namespace Beutl.UnitTests.Core;

public class CoreListTests
{
    // How InsertXyz reaches InsertRange(int, ReadOnlySpan<T>). Collection-expression arguments bind to the
    // span overloads, so list.AddRange(["x", "y"]) takes this path as well.
    public enum SpanRangeCall
    {
        AddRangeSpan,
        AddRangeCollectionExpression,
        InsertRangeSpan,
        InsertRangeCollectionExpression,
    }

    [Test]
    public void Add_RaisesAddCollectionChangedAndCountProperty()
    {
        var list = new CoreList<int>();
        var actions = new List<NotifyCollectionChangedAction>();
        var properties = new List<string?>();

        list.CollectionChanged += (_, e) => actions.Add(e.Action);
        list.PropertyChanged += (_, e) => properties.Add(e.PropertyName);

        list.Add(42);

        Assert.Multiple(() =>
        {
            Assert.That(list.Count, Is.EqualTo(1));
            Assert.That(actions, Is.EqualTo(new[] { NotifyCollectionChangedAction.Add }));
            Assert.That(properties, Does.Contain(nameof(CoreList<int>.Count)));
            Assert.That(properties, Does.Contain("Item[]"));
        });
    }

    [Test]
    public void Add_FiresAttachedEvent()
    {
        var list = new CoreList<string>();
        var attached = new List<string>();
        list.Attached += attached.Add;

        list.Add("hello");
        list.AddRange(new[] { "a", "b" });

        Assert.That(attached, Is.EqualTo(new[] { "hello", "a", "b" }));
    }

    [Test]
    public void Indexer_SettingNewValue_FiresReplaceAndDetached()
    {
        var list = new CoreList<int>(new[] { 1, 2, 3 });
        var actions = new List<NotifyCollectionChangedAction>();
        var detached = new List<int>();
        var attached = new List<int>();

        list.CollectionChanged += (_, e) => actions.Add(e.Action);
        list.Detached += detached.Add;
        list.Attached += attached.Add;

        list[1] = 99;

        Assert.Multiple(() =>
        {
            Assert.That(list[1], Is.EqualTo(99));
            Assert.That(actions, Is.EqualTo(new[] { NotifyCollectionChangedAction.Replace }));
            Assert.That(detached, Is.EqualTo(new[] { 2 }));
            Assert.That(attached, Is.EqualTo(new[] { 99 }));
        });
    }

    [Test]
    public void Indexer_SettingSameValue_DoesNotRaiseEvents()
    {
        var list = new CoreList<int>(new[] { 1, 2 });
        var actions = new List<NotifyCollectionChangedAction>();
        list.CollectionChanged += (_, e) => actions.Add(e.Action);

        list[0] = 1;

        Assert.That(actions, Is.Empty);
    }

    [Test]
    public void Insert_AddsItemAtIndex()
    {
        var list = new CoreList<int>(new[] { 1, 3 });
        var args = new List<NotifyCollectionChangedEventArgs>();
        list.CollectionChanged += (_, e) => args.Add(e);

        list.Insert(1, 2);

        Assert.Multiple(() =>
        {
            Assert.That(list, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(args, Has.Count.EqualTo(1));
            Assert.That(args[0].Action, Is.EqualTo(NotifyCollectionChangedAction.Add));
            Assert.That(args[0].NewStartingIndex, Is.EqualTo(1));
        });
    }

    [Test]
    public void Remove_ReturnsTrueAndFiresRemove()
    {
        var list = new CoreList<int>(new[] { 1, 2, 3 });
        var detached = new List<int>();
        list.Detached += detached.Add;

        Assert.Multiple(() =>
        {
            Assert.That(list.Remove(2), Is.True);
            Assert.That(list, Is.EqualTo(new[] { 1, 3 }));
            Assert.That(detached, Is.EqualTo(new[] { 2 }));
            Assert.That(list.Remove(99), Is.False);
        });
    }

    [Test]
    public void Move_ChangesPosition()
    {
        var list = new CoreList<int>(new[] { 1, 2, 3, 4 });
        list.Move(0, 3);
        Assert.That(list, Is.EqualTo(new[] { 2, 3, 4, 1 }));
    }

    [Test]
    public void MoveRange_MovesContiguousItems()
    {
        var list = new CoreList<int>(new[] { 1, 2, 3, 4, 5 });
        list.MoveRange(0, 2, 5);
        Assert.That(list, Is.EqualTo(new[] { 3, 4, 5, 1, 2 }));
    }

    [Test]
    public void RemoveAll_RemovesEveryListedItem()
    {
        var list = new CoreList<int>(new[] { 1, 2, 3, 2, 4, 2 });
        list.RemoveAll(new[] { 2 });
        Assert.That(list, Is.EqualTo(new[] { 1, 3, 4 }));
    }

    [Test]
    public void Replace_DetachesAndAttachesItems()
    {
        var list = new CoreList<int>(new[] { 1, 2 });
        var attached = new List<int>();
        var detached = new List<int>();
        list.Attached += attached.Add;
        list.Detached += detached.Add;

        list.Replace(new[] { 9, 8, 7 });

        Assert.Multiple(() =>
        {
            Assert.That(list, Is.EqualTo(new[] { 9, 8, 7 }));
            Assert.That(detached, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(attached, Is.EqualTo(new[] { 9, 8, 7 }));
        });
    }

    [Test]
    public void Clear_FiresRemoveOrResetBasedOnBehavior()
    {
        var removeList = new CoreList<int>(new[] { 1, 2 }) { ResetBehavior = ResetBehavior.Remove };
        var resetList = new CoreList<int>(new[] { 1, 2 }) { ResetBehavior = ResetBehavior.Reset };

        var removeActions = new List<NotifyCollectionChangedAction>();
        var resetActions = new List<NotifyCollectionChangedAction>();
        removeList.CollectionChanged += (_, e) => removeActions.Add(e.Action);
        resetList.CollectionChanged += (_, e) => resetActions.Add(e.Action);

        removeList.Clear();
        resetList.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(removeList.Count, Is.EqualTo(0));
            Assert.That(resetList.Count, Is.EqualTo(0));
            Assert.That(removeActions, Is.EqualTo(new[] { NotifyCollectionChangedAction.Remove }));
            Assert.That(resetActions, Is.EqualTo(new[] { NotifyCollectionChangedAction.Reset }));
        });
    }

    [Test]
    public void Clear_OnEmpty_DoesNothing()
    {
        var list = new CoreList<int>();
        var fired = false;
        list.CollectionChanged += (_, _) => fired = true;
        list.Clear();
        Assert.That(fired, Is.False);
    }

    [Test]
    public void GetMarshal_ProvidesCurrentSpan()
    {
        var list = new CoreList<int>(new[] { 10, 20, 30 });
        CoreListMarshal<int> marshal = list.GetMarshal();
        Assert.That(marshal.Value.ToArray(), Is.EqualTo(new[] { 10, 20, 30 }));
    }

    [Test]
    public void GetMarshal_ThrowsAfterMutation()
    {
        var list = new CoreList<int>(new[] { 10 });
        CoreListMarshal<int> marshal = list.GetMarshal();
        list.Add(20);

        bool threw = false;
        try
        {
            _ = marshal.Value.Length;
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        Assert.That(threw, Is.True);
    }

    [Test]
    public void EnsureCapacity_GrowsBackingList()
    {
        var list = new CoreList<int>();
        list.EnsureCapacity(32);
        Assert.That(list.Capacity, Is.GreaterThanOrEqualTo(32));
    }

    [Test]
    public void IList_IsFixedSize_AndIsReadOnly_AreFalse()
    {
        System.Collections.IList list = new CoreList<int>();
        Assert.Multiple(() =>
        {
            Assert.That(list.IsFixedSize, Is.False);
            Assert.That(list.IsReadOnly, Is.False);
        });
    }

    [TestCase(SpanRangeCall.AddRangeSpan, 3, new[] { "a", "b", "c", "x", "y", "z" })]
    [TestCase(SpanRangeCall.AddRangeCollectionExpression, 3, new[] { "a", "b", "c", "x", "y", "z" })]
    [TestCase(SpanRangeCall.InsertRangeSpan, 1, new[] { "a", "x", "y", "z", "b", "c" })]
    [TestCase(SpanRangeCall.InsertRangeCollectionExpression, 1, new[] { "a", "x", "y", "z", "b", "c" })]
    public void SpanRange_RaisesAddWithExactlyTheInsertedItems(SpanRangeCall call, int index, string[] expected)
    {
        var list = new SpanRangeRecordingList { "a", "b", "c" };
        var args = new List<NotifyCollectionChangedEventArgs>();
        list.CollectionChanged += (_, e) => args.Add(e);

        InsertXyz(list, call);

        Assert.Multiple(() =>
        {
            Assert.That(list.SpanRangeCalls, Is.EqualTo(1));
            Assert.That(list, Is.EqualTo(expected));
            Assert.That(args, Has.Count.EqualTo(1));
            Assert.That(args[0].Action, Is.EqualTo(NotifyCollectionChangedAction.Add));
            Assert.That(args[0].NewStartingIndex, Is.EqualTo(index));
            Assert.That(args[0].NewItems, Has.Count.EqualTo(3));
            Assert.That(args[0].NewItems, Is.EqualTo(new[] { "x", "y", "z" }));
        });
    }

    [Test]
    public void SpanRange_KeepsEarlierNewItemsIntact([Values] SpanRangeCall call)
    {
        var list = new SpanRangeRecordingList { "a", "b", "c" };
        var newItems = new List<IList?>();
        list.CollectionChanged += (_, e) => newItems.Add(e.NewItems);

        InsertXyz(list, call);
        // Same length but different items, so a buffer shared between the two notifications shows up.
        list.AddRange(["p", "q", "r"]);

        Assert.Multiple(() =>
        {
            Assert.That(list.SpanRangeCalls, Is.EqualTo(2));
            Assert.That(newItems, Has.Count.EqualTo(2));
            Assert.That(newItems[0], Is.EqualTo(new[] { "x", "y", "z" }));
            Assert.That(newItems[1], Is.EqualTo(new[] { "p", "q", "r" }));
        });
    }

    [Test]
    public void SpanRange_RaisesAttachedAndPropertyChanged(
        [Values] SpanRangeCall call,
        [Values] bool withCollectionChangedHandler)
    {
        var list = new SpanRangeRecordingList { "a", "b", "c" };
        var attached = new List<string>();
        var properties = new List<string?>();
        list.Attached += attached.Add;
        list.PropertyChanged += (_, e) => properties.Add(e.PropertyName);
        if (withCollectionChangedHandler)
        {
            list.CollectionChanged += (_, _) => { };
        }

        InsertXyz(list, call);

        Assert.Multiple(() =>
        {
            Assert.That(list.SpanRangeCalls, Is.EqualTo(1));
            Assert.That(attached, Is.EqualTo(new[] { "x", "y", "z" }));
            Assert.That(properties, Is.EqualTo(new[] { "Item[]", nameof(CoreList<string>.Count) }));
        });
    }

    [Test]
    public void SpanRange_KeepsDynamicDataMirrorEqualToTheList([Values] SpanRangeCall call)
    {
        var list = new SpanRangeRecordingList { "a", "b", "c" };
        using IDisposable subscription = list
            .ToObservableChangeSet<CoreList<string>, string>()
            .Bind(out ReadOnlyObservableCollection<string> mirror)
            .Subscribe();

        InsertXyz(list, call);
        Assert.That(mirror, Is.EqualTo(list));

        InsertXyz(list, call);
        list.InsertRange(0, ["p"]);
        Assert.Multiple(() =>
        {
            Assert.That(list.SpanRangeCalls, Is.EqualTo(3));
            Assert.That(list, Has.Count.EqualTo(10));
            Assert.That(mirror, Is.EqualTo(list));
        });
    }

    // Inserts x, y and z through the span overload, at the end (AddRange) or at index 1 (InsertRange).
    private static void InsertXyz(CoreList<string> list, SpanRangeCall call)
    {
        // A slice, so that the span is shorter than the array behind it.
        ReadOnlySpan<string> xyz = new[] { "w", "x", "y", "z", "v" }.AsSpan(1, 3);
        switch (call)
        {
            case SpanRangeCall.AddRangeSpan:
                list.AddRange(xyz);
                break;
            case SpanRangeCall.AddRangeCollectionExpression:
                list.AddRange(["x", "y", "z"]);
                break;
            case SpanRangeCall.InsertRangeSpan:
                list.InsertRange(1, xyz);
                break;
            case SpanRangeCall.InsertRangeCollectionExpression:
                list.InsertRange(1, ["x", "y", "z"]);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(call), call, null);
        }
    }

    // Counts calls to the span overload, which AddRange(ReadOnlySpan<T>) also goes through, so each test can
    // show that its insertions took that path.
    private sealed class SpanRangeRecordingList : CoreList<string>
    {
        public int SpanRangeCalls { get; private set; }

        public override void InsertRange(int index, ReadOnlySpan<string> items)
        {
            SpanRangeCalls++;
            base.InsertRange(index, items);
        }
    }
}
