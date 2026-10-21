using System.Buffers;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reactive;
using Beutl.Collections;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Operations;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor;

public class CollectionOperationObserverTests
{
    private OperationSequenceGenerator _sequenceGenerator = null!;

    [SetUp]
    public void Setup()
    {
        _sequenceGenerator = new OperationSequenceGenerator();
    }

    #region Constructor Tests

    [Test]
    public void Constructor_WithExistingItems_ShouldInitializeChildPublishers()
    {
        var owner = new TestOwnerCoreObject();
        var item = new TestItemCoreObject { Title = "initial" };
        var list = new CoreList<TestItemCoreObject> { item };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        // Modify existing item - should be tracked
        item.Title = "modified";

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        Assert.That(receivedOperations[0], Is.TypeOf<UpdatePropertyValueOperation<string>>());
    }

    [Test]
    public void Constructor_WithPropertyPath_ShouldBuildCorrectPath()
    {
        var owner = new TestOwnerCoreObject();
        var item = new TestItemCoreObject();
        var list = new CoreList<TestItemCoreObject> { item };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Parent.Items", _sequenceGenerator);
        item.Title = "changed";

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<string>)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("Parent.Items.Title"));
    }

    #endregion

    #region Add Operation Tests (EnqueueAdds)

    [Test]
    public void Add_SingleItem_ShouldPublishInsertCollectionRangeOperation()
    {
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        var item = new TestItemCoreObject { Title = "item1" };
        list.Add(item);

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (InsertCollectionRangeOperation<TestItemCoreObject>)receivedOperations[0];
        Assert.Multiple(() =>
        {
            Assert.That(operation.Object, Is.SameAs(owner));
            Assert.That(operation.PropertyPath, Is.EqualTo("Items"));
            Assert.That(operation.Items, Has.Length.EqualTo(1));
            Assert.That(operation.Items[0], Is.SameAs(item));
            Assert.That(operation.Index, Is.EqualTo(0));
        });
    }

    [Test]
    public void Add_MultipleTimes_ShouldPublishMultipleOperations()
    {
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        list.Add(new TestItemCoreObject { Title = "item1" });
        list.Add(new TestItemCoreObject { Title = "item2" });
        list.Add(new TestItemCoreObject { Title = "item3" });

        Assert.That(receivedOperations, Has.Count.EqualTo(3));
        var op1 = (InsertCollectionRangeOperation<TestItemCoreObject>)receivedOperations[0];
        var op2 = (InsertCollectionRangeOperation<TestItemCoreObject>)receivedOperations[1];
        var op3 = (InsertCollectionRangeOperation<TestItemCoreObject>)receivedOperations[2];
        Assert.Multiple(() =>
        {
            Assert.That(op1.Index, Is.EqualTo(0));
            Assert.That(op2.Index, Is.EqualTo(1));
            Assert.That(op3.Index, Is.EqualTo(2));
            Assert.That(op2.SequenceNumber, Is.GreaterThan(op1.SequenceNumber));
            Assert.That(op3.SequenceNumber, Is.GreaterThan(op2.SequenceNumber));
        });
    }

    [Test]
    public void Add_CoreObjectItem_ShouldCreateChildPublisher()
    {
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        var item = new TestItemCoreObject();
        list.Add(item);
        receivedOperations.Clear();

        item.Title = "modified";

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        Assert.That(receivedOperations[0], Is.TypeOf<UpdatePropertyValueOperation<string>>());
    }

    [Test]
    public void Insert_AtSpecificIndex_ShouldPublishCorrectIndex()
    {
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>
        {
            new() { Title = "item1" },
            new() { Title = "item3" }
        };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);
        receivedOperations.Clear();

        var item = new TestItemCoreObject { Title = "item2" };
        list.Insert(1, item);

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (InsertCollectionRangeOperation<TestItemCoreObject>)receivedOperations[0];
        Assert.That(operation.Index, Is.EqualTo(1));
    }

    [Test]
    public void AddRange_ShouldPublishWithAllItems()
    {
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        var items = new[]
        {
            new TestItemCoreObject { Title = "item1" },
            new TestItemCoreObject { Title = "item2" },
            new TestItemCoreObject { Title = "item3" }
        };
        list.AddRange(items);

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (InsertCollectionRangeOperation<TestItemCoreObject>)receivedOperations[0];
        Assert.That(operation.Items, Has.Length.EqualTo(3));
    }

    #endregion

    #region Remove Operation Tests (EnqueueRemoveRange)

    [Test]
    public void Remove_SingleItem_ShouldPublishRemoveCollectionRangeOperation()
    {
        var owner = new TestOwnerCoreObject();
        var item = new TestItemCoreObject { Title = "item1" };
        var list = new CoreList<TestItemCoreObject> { item };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);
        receivedOperations.Clear();

        list.Remove(item);

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (RemoveCollectionRangeOperation<TestItemCoreObject>)receivedOperations[0];
        Assert.Multiple(() =>
        {
            Assert.That(operation.Object, Is.SameAs(owner));
            Assert.That(operation.PropertyPath, Is.EqualTo("Items"));
            Assert.That(operation.Items, Has.Length.EqualTo(1));
            Assert.That(operation.Items[0], Is.SameAs(item));
            Assert.That(operation.Index, Is.EqualTo(0));
        });
    }

    [Test]
    public void RemoveAt_ShouldPublishCorrectIndex()
    {
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>
        {
            new() { Title = "item1" },
            new() { Title = "item2" },
            new() { Title = "item3" }
        };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);
        receivedOperations.Clear();

        list.RemoveAt(1);

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (RemoveCollectionRangeOperation<TestItemCoreObject>)receivedOperations[0];
        Assert.Multiple(() =>
        {
            Assert.That(operation.Index, Is.EqualTo(1));
            Assert.That(operation.Items[0].Title, Is.EqualTo("item2"));
        });
    }

    [Test]
    public void RemoveRange_ShouldPublishWithAllRemovedItems()
    {
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>
        {
            new() { Title = "item1" },
            new() { Title = "item2" },
            new() { Title = "item3" },
            new() { Title = "item4" }
        };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);
        receivedOperations.Clear();

        list.RemoveRange(1, 2);

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (RemoveCollectionRangeOperation<TestItemCoreObject>)receivedOperations[0];
        Assert.Multiple(() =>
        {
            Assert.That(operation.Index, Is.EqualTo(1));
            Assert.That(operation.Items, Has.Length.EqualTo(2));
        });
    }

    [Test]
    public void Clear_WithResetBehaviorRemove_ShouldPublishRemoveAllItems()
    {
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>
        {
            new() { Title = "item1" },
            new() { Title = "item2" }
        };
        list.ResetBehavior = ResetBehavior.Remove; // Required to get Remove event instead of Reset
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);
        receivedOperations.Clear();

        list.Clear();

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (RemoveCollectionRangeOperation<TestItemCoreObject>)receivedOperations[0];
        Assert.That(operation.Items, Has.Length.EqualTo(2));
    }

    #endregion

    #region Reset Operation Tests (EnqueueReset)

    // CoreList<T> defaults to ResetBehavior.Reset, so its Clear raises a Reset that carries no items.

    [Test]
    public void Clear_WithDefaultResetBehavior_ShouldPublishRemoveOfAllItems()
    {
        var owner = new TestOwnerCoreObject();
        var item1 = new TestItemCoreObject { Title = "item1" };
        var item2 = new TestItemCoreObject { Title = "item2" };
        var list = new CoreList<TestItemCoreObject> { item1, item2 };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        list.Clear();

        // The same operation a list with ResetBehavior.Remove produces
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (RemoveCollectionRangeOperation<TestItemCoreObject>)receivedOperations[0];
        Assert.Multiple(() =>
        {
            Assert.That(operation.Object, Is.SameAs(owner));
            Assert.That(operation.PropertyPath, Is.EqualTo("Items"));
            Assert.That(operation.Index, Is.Zero);
            Assert.That(operation.Items, Is.EqualTo(new[] { item1, item2 }));
        });
    }

    [Test]
    public void Reset_ShouldPublishRemoveOfOldItemsAndInsertOfNewItems()
    {
        var owner = new TestOwnerCoreObject();
        var kept = new TestItemCoreObject { Title = "kept" };
        var removed = new TestItemCoreObject { Title = "removed" };
        var added = new TestItemCoreObject { Title = "added" };
        var list = new ManuallyNotifyingCollection<TestItemCoreObject>([kept, removed]);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        list.ResetTo([added, kept]);

        Assert.That(receivedOperations, Has.Count.EqualTo(2));
        var remove = (RemoveCollectionRangeOperation<TestItemCoreObject>)receivedOperations[0];
        var insert = (InsertCollectionRangeOperation<TestItemCoreObject>)receivedOperations[1];
        Assert.Multiple(() =>
        {
            Assert.That(remove.Index, Is.Zero);
            Assert.That(remove.Items, Is.EqualTo(new[] { kept, removed }));
            Assert.That(insert.Index, Is.Zero);
            Assert.That(insert.Items, Is.EqualTo(new[] { added, kept }));
        });
    }

    [Test]
    public void Reset_WithoutChange_ShouldNotPublish()
    {
        var owner = new TestOwnerCoreObject();
        var item = new TestItemCoreObject();
        var list = new ManuallyNotifyingCollection<TestItemCoreObject>([item]);
        var emptyList = new ManuallyNotifyingCollection<TestItemCoreObject>([]);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);
        using var emptyListObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, emptyList, owner, "Items", _sequenceGenerator);

        list.ResetTo([item]);
        emptyList.Clear(); // ObservableCollection raises Reset even when it is already empty

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Reset_ShouldTrackExactlyTheItemsInTheList()
    {
        var owner = new TestOwnerCoreObject();
        var kept = new TestItemCoreObject();
        var removed = new TestItemCoreObject();
        var added = new TestItemCoreObject();
        var list = new ManuallyNotifyingCollection<TestItemCoreObject>([kept, removed]);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);
        list.ResetTo([kept, added]);
        receivedOperations.Clear();

        removed.Title = "removed";
        kept.Title = "kept";
        added.Title = "added";

        // Each item in the list is tracked once, and the removed one is not tracked
        Assert.That(
            receivedOperations.Cast<UpdatePropertyValueOperation<string>>().Select(op => op.Object),
            Is.EqualTo(new[] { kept, added }));
    }

    [Test]
    public void Add_AfterClearWithDefaultResetBehavior_ShouldTrackReaddedItem()
    {
        var owner = new TestOwnerCoreObject();
        var item = new TestItemCoreObject();
        var list = new CoreList<TestItemCoreObject> { item };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);
        list.Clear();
        item.Title = "untracked";

        list.Add(item);
        item.Title = "tracked";

        Assert.That(receivedOperations, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            Assert.That(receivedOperations[0], Is.TypeOf<RemoveCollectionRangeOperation<TestItemCoreObject>>());
            Assert.That(receivedOperations[1], Is.TypeOf<InsertCollectionRangeOperation<TestItemCoreObject>>());
            Assert.That(receivedOperations[2], Is.TypeOf<UpdatePropertyValueOperation<string>>());
            Assert.That(((UpdatePropertyValueOperation<string>)receivedOperations[2]).NewValue, Is.EqualTo("tracked"));
        });
    }

    [TestCase(NotifyCollectionChangedAction.Add)]
    [TestCase(NotifyCollectionChangedAction.Remove)]
    [TestCase(NotifyCollectionChangedAction.Move)]
    [TestCase(NotifyCollectionChangedAction.Replace)]
    [TestCase(NotifyCollectionChangedAction.Reset)]
    public void Clear_AfterSuppressedChange_ShouldPublishCurrentItems(NotifyCollectionChangedAction suppressedChange)
    {
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>
        {
            new() { Title = "item1" },
            new() { Title = "item2" },
            new() { Title = "item3" }
        };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        // Changes made while publishing is suppressed (as KeyFrameAnimation<T> re-sorts its key frames)
        // are not published, but a later Reset still has to report what the list held.
        using (PublishingSuppression.Enter())
        {
            switch (suppressedChange)
            {
                case NotifyCollectionChangedAction.Add:
                    list.Insert(1, new TestItemCoreObject { Title = "added" });
                    break;
                case NotifyCollectionChangedAction.Remove:
                    list.RemoveAt(1);
                    break;
                case NotifyCollectionChangedAction.Move:
                    list.Move(0, 2);
                    break;
                case NotifyCollectionChangedAction.Replace:
                    list[1] = new TestItemCoreObject { Title = "replaced" };
                    break;
                case NotifyCollectionChangedAction.Reset:
                    list.Clear();
                    list.Add(new TestItemCoreObject { Title = "added" });
                    break;
            }
        }

        TestItemCoreObject[] current = [.. list];

        list.Clear();

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (RemoveCollectionRangeOperation<TestItemCoreObject>)receivedOperations[0];
        Assert.That(operation.Items, Is.EqualTo(current));
    }

    [Test]
    public void Clear_AfterAddRangeOfSpan_ShouldPublishCurrentItems()
    {
        var owner = new TestOwnerCoreObject();
        TestItemCoreObject[] items = [new() { Title = "item1" }, new() { Title = "item2" }];
        var list = new CoreList<TestItemCoreObject>();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);
        // This overload notifies its pooled array, which is longer than the two items it adds.
        list.AddRange(new ReadOnlySpan<TestItemCoreObject>(items));
        receivedOperations.Clear();

        list.Clear();

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (RemoveCollectionRangeOperation<TestItemCoreObject>)receivedOperations[0];
        Assert.That(operation.Items, Is.EqualTo(items));
    }

    [Test]
    public void AddRangeOfSpan_ShouldTrackOnlyTheItemsInTheList([Values] bool suppressed)
    {
        var owner = new TestOwnerCoreObject();
        var existing = new TestItemCoreObject();
        var added = new TestItemCoreObject();
        var stale = new TestItemCoreObject();
        var list = new CoreList<TestItemCoreObject> { existing };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        // AddRange(ReadOnlySpan<T>) notifies the array it rents from the shared pool, which is longer than the
        // items it adds. The array returned last on this thread is rented next, so the notification also lists
        // a stale item and an item that is already in the list.
        TestItemCoreObject[] pooled = ArrayPool<TestItemCoreObject>.Shared.Rent(1);
        Array.Clear(pooled);
        pooled[1] = stale;
        pooled[2] = existing;
        ArrayPool<TestItemCoreObject>.Shared.Return(pooled);
        ReadOnlySpan<TestItemCoreObject> addedItems = [added];

        using (suppressed ? PublishingSuppression.Enter() : null)
        {
            list.AddRange(addedItems);
        }

        receivedOperations.Clear();
        stale.Title = "stale";
        existing.Title = "existing";
        added.Title = "added";

        // Each item in the list is tracked once, and the stale item is not tracked
        Assert.That(
            receivedOperations.Cast<UpdatePropertyValueOperation<string>>().Select(op => op.Object),
            Is.EqualTo(new[] { existing, added }));
    }

    [Test]
    public void AddRangeOfSpan_UndoRedo_RestoresTheList()
    {
        var owner = new TestOwnerCoreObject();
        var existing = new TestItemCoreObject { Title = "existing" };
        var added = new TestItemCoreObject { Title = "added" };
        owner.Items.Add(existing);

        using var harness = new HistoryHarness(owner);

        // The notification lists more items than were added, so the change is recorded
        // from the list instead
        ReadOnlySpan<TestItemCoreObject> addedItems = [added];
        owner.Items.AddRange(addedItems);
        harness.History.Commit("add range");
        Assert.That(owner.Items, Is.EqualTo(new[] { existing, added }));

        Assert.That(harness.History.Undo(), Is.True);
        Assert.That(owner.Items, Is.EqualTo(new[] { existing }));

        Assert.That(harness.History.Redo(), Is.True);
        Assert.That(owner.Items, Is.EqualTo(new[] { existing, added }));
    }

    [Test]
    public void DuplicateItem_ShouldBeTrackedWhileTheListHoldsIt()
    {
        var owner = new TestOwnerCoreObject();
        var item = new TestItemCoreObject();
        var list = new CoreList<TestItemCoreObject> { item };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        // The same item twice in the list is tracked once
        list.Add(item);
        receivedOperations.Clear();
        item.Title = "in the list twice";
        Assert.That(receivedOperations, Has.Count.EqualTo(1));

        // One occurrence removed: the item is still in the list
        list.RemoveAt(1);
        receivedOperations.Clear();
        item.Title = "in the list once";
        Assert.That(receivedOperations, Has.Count.EqualTo(1));

        // The last occurrence removed
        list.RemoveAt(0);
        receivedOperations.Clear();
        item.Title = "not in the list";
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Reset_WithDuplicateItem_ShouldTrackItOnce()
    {
        var owner = new TestOwnerCoreObject();
        var item = new TestItemCoreObject();
        var list = new ManuallyNotifyingCollection<TestItemCoreObject>([]);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        list.ResetTo([item, item]);
        receivedOperations.Clear();
        item.Title = "in the list twice";

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    [Test]
    public void AddWithoutIndex_ShouldBeRecordedFromTheList()
    {
        var owner = new TestOwnerCoreObject();
        var existing = new TestItemCoreObject { Title = "existing" };
        var added = new TestItemCoreObject { Title = "added" };
        var list = new ManuallyNotifyingCollection<TestItemCoreObject>([existing]);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        // The notification does not say where the item went
        list.AddWithoutIndex(added);

        // Recorded as the change of the whole list
        Assert.That(receivedOperations, Has.Count.EqualTo(2));
        var remove = (RemoveCollectionRangeOperation<TestItemCoreObject>)receivedOperations[0];
        var insert = (InsertCollectionRangeOperation<TestItemCoreObject>)receivedOperations[1];
        Assert.Multiple(() =>
        {
            Assert.That(remove.Index, Is.Zero);
            Assert.That(remove.Items, Is.EqualTo(new[] { existing }));
            Assert.That(insert.Index, Is.Zero);
            Assert.That(insert.Items, Is.EqualTo(new[] { added, existing }));
        });
    }

    [Test]
    public void ItemsChangedWhilePublishingSuppressed_ShouldBeTrackedAsTheListHoldsThem()
    {
        var owner = new TestOwnerCoreObject();
        var removed = new TestItemCoreObject();
        var added = new TestItemCoreObject();
        var list = new CoreList<TestItemCoreObject> { removed };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);
        using (PublishingSuppression.Enter())
        {
            list.Remove(removed);
            list.Add(added);
        }

        removed.Title = "untracked";
        added.Title = "tracked";
        list.Add(removed);

        Assert.That(receivedOperations, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(receivedOperations[0], Is.TypeOf<UpdatePropertyValueOperation<string>>());
            Assert.That(((UpdatePropertyValueOperation<string>)receivedOperations[0]).Object, Is.SameAs(added));
            Assert.That(receivedOperations[1], Is.TypeOf<InsertCollectionRangeOperation<TestItemCoreObject>>());
        });
    }

    #endregion

    #region Move Operation Tests (EnqueueMove)

    [Test]
    public void Move_ShouldPublishMoveCollectionRangeOperation()
    {
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>
        {
            new() { Title = "item1" },
            new() { Title = "item2" },
            new() { Title = "item3" }
        };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);
        receivedOperations.Clear();

        list.Move(0, 2);

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (MoveCollectionRangeOperation<TestItemCoreObject>)receivedOperations[0];
        Assert.Multiple(() =>
        {
            Assert.That(operation.Object, Is.SameAs(owner));
            Assert.That(operation.PropertyPath, Is.EqualTo("Items"));
            Assert.That(operation.OldIndex, Is.EqualTo(0));
            Assert.That(operation.NewIndex, Is.EqualTo(2));
            Assert.That(operation.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void MoveRange_ShouldPublishWithCorrectCount()
    {
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>
        {
            new() { Title = "item1" },
            new() { Title = "item2" },
            new() { Title = "item3" },
            new() { Title = "item4" },
            new() { Title = "item5" }
        };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);
        receivedOperations.Clear();

        list.MoveRange(0, 2, 4);

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (MoveCollectionRangeOperation<TestItemCoreObject>)receivedOperations[0];
        // Note: CoreList adjusts newIndex when it's after oldIndex, so the event reports newIndex=2
        Assert.Multiple(() =>
        {
            Assert.That(operation.OldIndex, Is.EqualTo(0));
            Assert.That(operation.NewIndex, Is.EqualTo(2));
            Assert.That(operation.Count, Is.EqualTo(2));
        });
    }

    #endregion

    #region Replace Operation Tests (EnqueueReplace)

    [Test]
    public void Replace_ShouldPublishRemoveAndInsertOperations()
    {
        var owner = new TestOwnerCoreObject();
        var oldItem = new TestItemCoreObject { Title = "old" };
        var list = new CoreList<TestItemCoreObject> { oldItem };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);
        receivedOperations.Clear();

        var newItem = new TestItemCoreObject { Title = "new" };
        list[0] = newItem;

        Assert.That(receivedOperations, Has.Count.EqualTo(2));
        Assert.That(receivedOperations[0], Is.TypeOf<RemoveCollectionRangeOperation<TestItemCoreObject>>());
        Assert.That(receivedOperations[1], Is.TypeOf<InsertCollectionRangeOperation<TestItemCoreObject>>());
    }

    [Test]
    public void Replace_ShouldDisposeOldChildPublisherAndCreateNew()
    {
        var owner = new TestOwnerCoreObject();
        var oldItem = new TestItemCoreObject();
        var list = new CoreList<TestItemCoreObject> { oldItem };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        var newItem = new TestItemCoreObject();
        list[0] = newItem;
        receivedOperations.Clear();

        oldItem.Title = "old - not tracked";
        newItem.Title = "new - tracked";

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<string>)receivedOperations[0];
        Assert.That(operation.NewValue, Is.EqualTo("new - tracked"));
    }

    [Test]
    public void ReplaceAll_ShouldPublishRemoveAndInsertOperations([Values] ResetBehavior resetBehavior)
    {
        var owner = new TestOwnerCoreObject();
        var oldItem1 = new TestItemCoreObject { Title = "old1" };
        var oldItem2 = new TestItemCoreObject { Title = "old2" };
        // A list with ResetBehavior.Remove notifies a Remove and an Add, and one with ResetBehavior.Reset
        // notifies a single Reset. Both are recorded the same way.
        var list = new CoreList<TestItemCoreObject>(oldItem1, oldItem2) { ResetBehavior = resetBehavior };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);
        receivedOperations.Clear();

        var newItems = new List<TestItemCoreObject>
        {
            new() { Title = "new1" },
            new() { Title = "new2" },
            new() { Title = "new3" }
        };
        list.Replace(newItems);

        // Recorded as Remove + Insert of the whole list
        Assert.That(receivedOperations, Has.Count.EqualTo(2));
        var remove = (RemoveCollectionRangeOperation<TestItemCoreObject>)receivedOperations[0];
        var insert = (InsertCollectionRangeOperation<TestItemCoreObject>)receivedOperations[1];
        Assert.Multiple(() =>
        {
            Assert.That(remove.Index, Is.Zero);
            Assert.That(remove.Items, Is.EqualTo(new[] { oldItem1, oldItem2 }));
            Assert.That(insert.Index, Is.Zero);
            Assert.That(insert.Items, Is.EqualTo(newItems));
        });
    }

    [Test]
    public void ReplaceWithEmptyList_ShouldPublishOnlyRemoveOperation([Values] ResetBehavior resetBehavior)
    {
        var owner = new TestOwnerCoreObject();
        var oldItem1 = new TestItemCoreObject();
        var oldItem2 = new TestItemCoreObject();
        var list = new CoreList<TestItemCoreObject>(oldItem1, oldItem2) { ResetBehavior = resetBehavior };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        list.Replace([]);
        oldItem1.Title = "not tracked";

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var remove = (RemoveCollectionRangeOperation<TestItemCoreObject>)receivedOperations[0];
        Assert.Multiple(() =>
        {
            Assert.That(remove.Index, Is.Zero);
            Assert.That(remove.Items, Is.EqualTo(new[] { oldItem1, oldItem2 }));
        });
    }

    [Test]
    public void ReplaceOnEmptyList_ShouldPublishOnlyInsertOperation([Values] ResetBehavior resetBehavior)
    {
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject> { ResetBehavior = resetBehavior };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);
        var newItem = new TestItemCoreObject();

        list.Replace([newItem]);
        newItem.Title = "tracked";

        Assert.That(receivedOperations, Has.Count.EqualTo(2));
        var insert = (InsertCollectionRangeOperation<TestItemCoreObject>)receivedOperations[0];
        Assert.Multiple(() =>
        {
            Assert.That(insert.Index, Is.Zero);
            Assert.That(insert.Items, Is.EqualTo(new[] { newItem }));
            Assert.That(receivedOperations[1], Is.TypeOf<UpdatePropertyValueOperation<string>>());
        });
    }

    [TestCase(ResetBehavior.Remove, 2, 0)]
    [TestCase(ResetBehavior.Remove, 3, 1)]
    [TestCase(ResetBehavior.Remove, 1, 3)]
    [TestCase(ResetBehavior.Remove, 0, 2)]
    [TestCase(ResetBehavior.Reset, 2, 0)]
    [TestCase(ResetBehavior.Reset, 3, 1)]
    [TestCase(ResetBehavior.Reset, 1, 3)]
    [TestCase(ResetBehavior.Reset, 0, 2)]
    public void Replace_UndoRedo_RestoresBothLists(ResetBehavior resetBehavior, int oldCount, int newCount)
    {
        var owner = new TestOwnerCoreObject();
        TestItemCoreObject[] oldItems = CreateItems("old", oldCount);
        // When both lists have items, the first old item stays, so it is removed and inserted again.
        int kept = Math.Min(1, Math.Min(oldCount, newCount));
        TestItemCoreObject[] newItems = [.. oldItems.Take(kept), .. CreateItems("new", newCount - kept)];
        owner.Items = new CoreList<TestItemCoreObject>(oldItems) { ResetBehavior = resetBehavior };

        using var harness = new HistoryHarness(owner);

        owner.Items.Replace(newItems);
        harness.History.Commit("replace");
        Assert.That(owner.Items, Is.EqualTo(newItems));

        Assert.That(harness.History.Undo(), Is.True);
        Assert.That(owner.Items, Is.EqualTo(oldItems));

        Assert.That(harness.History.Redo(), Is.True);
        Assert.That(owner.Items, Is.EqualTo(newItems));

        static TestItemCoreObject[] CreateItems(string prefix, int count)
            => [.. Enumerable.Range(0, count).Select(i => new TestItemCoreObject { Title = $"{prefix}{i}" })];
    }

    #endregion

    #region Dispose Tests

    [Test]
    public void Dispose_ShouldDisposeAllChildPublishers()
    {
        var owner = new TestOwnerCoreObject();
        var item1 = new TestItemCoreObject();
        var item2 = new TestItemCoreObject();
        var list = new CoreList<TestItemCoreObject> { item1, item2 };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        operationObserver.Dispose();
        item1.Title = "should not be tracked";
        item2.Title = "should not be tracked";

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Dispose_ShouldUnsubscribeFromCollectionChanges()
    {
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        operationObserver.Dispose();
        list.Add(new TestItemCoreObject());

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Dispose_ShouldCompleteOperationsObservable()
    {
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>();
        var completed = false;
        var testObserver = Observer.Create<ChangeOperation>(_ => { });

        var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        operationObserver.Operations.Subscribe(_ => { }, () => completed = true);

        operationObserver.Dispose();

        Assert.That(completed, Is.True);
    }

    #endregion

    #region PropertyPathsToTrack Tests

    [Test]
    public void Constructor_WithPropertyPathsToTrack_ShouldFilterChildPublisherPaths()
    {
        var owner = new TestOwnerCoreObject();
        var item = new TestItemCoreObject();
        var list = new CoreList<TestItemCoreObject> { item };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        var propertyPathsToTrack = new HashSet<string> { "Items.Title" };

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator, propertyPathsToTrack);

        item.Title = "tracked";
        item.Value = 100; // Not in paths to track

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<string>)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("Items.Title"));
    }

    [Test]
    public void AddedItem_WithPropertyPathsToTrack_ShouldUseFilteredChildPublisher()
    {
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        var propertyPathsToTrack = new HashSet<string> { "Items.Title" };

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator, propertyPathsToTrack);

        var item = new TestItemCoreObject();
        list.Add(item);
        receivedOperations.Clear();

        item.Title = "tracked";
        item.Value = 100;

        // Only the Title change should be tracked
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        Assert.That(receivedOperations[0], Is.TypeOf<UpdatePropertyValueOperation<string>>());
    }

    #endregion

    #region Test Helper Classes

    private class TestOwnerCoreObject : CoreObject
    {
        public static readonly CoreProperty<CoreList<TestItemCoreObject>> ItemsProperty;

        private CoreList<TestItemCoreObject> _items = new();

        static TestOwnerCoreObject()
        {
            ItemsProperty = ConfigureProperty<CoreList<TestItemCoreObject>, TestOwnerCoreObject>(nameof(Items))
                .Accessor(o => o.Items, (o, v) => o.Items = v)
                .Register();
        }

        public CoreList<TestItemCoreObject> Items
        {
            get => _items;
            set => SetAndRaise(ItemsProperty, ref _items, value);
        }
    }

    private class TestItemCoreObject : CoreObject
    {
        public static readonly CoreProperty<string?> TitleProperty;
        public static readonly CoreProperty<int> ValueProperty;

        private string? _title;
        private int _value;

        static TestItemCoreObject()
        {
            TitleProperty = ConfigureProperty<string?, TestItemCoreObject>(nameof(Title))
                .Accessor(o => o.Title, (o, v) => o.Title = v)
                .Register();

            ValueProperty = ConfigureProperty<int, TestItemCoreObject>(nameof(Value))
                .Accessor(o => o.Value, (o, v) => o.Value = v)
                .Register();
        }

        public string? Title
        {
            get => _title;
            set => SetAndRaise(TitleProperty, ref _title, value);
        }

        public int Value
        {
            get => _value;
            set => SetAndRaise(ValueProperty, ref _value, value);
        }
    }

    private sealed class ManuallyNotifyingCollection<T>(IEnumerable<T> initialItems)
        : ObservableCollection<T>(initialItems)
    {
        // Swaps the items and raises a single Reset, as a CoreList with ResetBehavior.Reset does.
        public void ResetTo(IEnumerable<T> items)
        {
            Items.Clear();
            foreach (T item in items)
            {
                Items.Add(item);
            }

            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        // Raises an Add without the index of the item (-1), which a notification is allowed to omit.
        public void AddWithoutIndex(T item)
        {
            Items.Insert(0, item);
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item));
        }
    }

    #endregion
}
