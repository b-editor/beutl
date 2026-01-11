using System.Reactive;
using Beutl.Collections;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Operations;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Editor;

public class CollectionOperationObserverTests
{
    private OperationSequenceGenerator _sequenceGenerator = null!;

    [SetUp]
    public void Setup()
    {
        Log.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
        _sequenceGenerator = new OperationSequenceGenerator();
    }

    #region Constructor Tests

    [Test]
    public void Constructor_ShouldInitializeOperationsProperty()
    {
        // Arrange
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>();
        var testObserver = Observer.Create<ChangeOperation>(_ => { });

        // Act
        using var observer = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        // Assert
        Assert.That(observer.Operations, Is.Not.Null);
    }

    [Test]
    public void Constructor_WithObserver_ShouldSubscribeToOperations()
    {
        // Arrange
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        // Act
        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);
        list.Add(new TestItemCoreObject());

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        Assert.That(receivedOperations[0], Is.TypeOf<InsertCollectionRangeOperation<TestItemCoreObject>>());
    }

    [Test]
    public void Constructor_WithExistingItems_ShouldInitializeChildPublishers()
    {
        // Arrange
        var owner = new TestOwnerCoreObject();
        var item = new TestItemCoreObject { Title = "initial" };
        var list = new CoreList<TestItemCoreObject> { item };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        // Act
        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        // Modify existing item - should be tracked
        item.Title = "modified";

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        Assert.That(receivedOperations[0], Is.TypeOf<UpdatePropertyValueOperation<string>>());
    }

    [Test]
    public void Constructor_WithNonCoreObjectItems_ShouldNotCreateChildPublishers()
    {
        // Arrange
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<int> { 1, 2, 3 };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        // Act & Assert - should not throw
        Assert.DoesNotThrow(() =>
        {
            using var operationObserver = new CollectionOperationObserver<int>(
                testObserver, list, owner, "Items", _sequenceGenerator);
        });
    }

    [Test]
    public void Constructor_WithPropertyPath_ShouldBuildCorrectPath()
    {
        // Arrange
        var owner = new TestOwnerCoreObject();
        var item = new TestItemCoreObject();
        var list = new CoreList<TestItemCoreObject> { item };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        // Act
        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Parent.Items", _sequenceGenerator);
        item.Title = "changed";

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<string>)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("Parent.Items.Title"));
    }

    #endregion

    #region Add Operation Tests (EnqueueAdds)

    [Test]
    public void Add_SingleItem_ShouldPublishInsertCollectionRangeOperation()
    {
        // Arrange
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        // Act
        var item = new TestItemCoreObject { Title = "item1" };
        list.Add(item);

        // Assert
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
        // Arrange
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        // Act
        list.Add(new TestItemCoreObject { Title = "item1" });
        list.Add(new TestItemCoreObject { Title = "item2" });
        list.Add(new TestItemCoreObject { Title = "item3" });

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(3));
        var op1 = (InsertCollectionRangeOperation<TestItemCoreObject>)receivedOperations[0];
        var op2 = (InsertCollectionRangeOperation<TestItemCoreObject>)receivedOperations[1];
        var op3 = (InsertCollectionRangeOperation<TestItemCoreObject>)receivedOperations[2];
        Assert.Multiple(() =>
        {
            Assert.That(op1.Index, Is.EqualTo(0));
            Assert.That(op2.Index, Is.EqualTo(1));
            Assert.That(op3.Index, Is.EqualTo(2));
        });
    }

    [Test]
    public void Add_CoreObjectItem_ShouldCreateChildPublisher()
    {
        // Arrange
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        var item = new TestItemCoreObject();
        list.Add(item);
        receivedOperations.Clear();

        // Act
        item.Title = "modified";

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        Assert.That(receivedOperations[0], Is.TypeOf<UpdatePropertyValueOperation<string>>());
    }

    [Test]
    public void Insert_AtSpecificIndex_ShouldPublishCorrectIndex()
    {
        // Arrange
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

        // Act
        var item = new TestItemCoreObject { Title = "item2" };
        list.Insert(1, item);

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (InsertCollectionRangeOperation<TestItemCoreObject>)receivedOperations[0];
        Assert.That(operation.Index, Is.EqualTo(1));
    }

    [Test]
    public void AddRange_ShouldPublishWithAllItems()
    {
        // Arrange
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        // Act
        var items = new[]
        {
            new TestItemCoreObject { Title = "item1" },
            new TestItemCoreObject { Title = "item2" },
            new TestItemCoreObject { Title = "item3" }
        };
        list.AddRange(items);

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (InsertCollectionRangeOperation<TestItemCoreObject>)receivedOperations[0];
        Assert.That(operation.Items, Has.Length.EqualTo(3));
    }

    #endregion

    #region Remove Operation Tests (EnqueueRemoveRange)

    [Test]
    public void Remove_SingleItem_ShouldPublishRemoveCollectionRangeOperation()
    {
        // Arrange
        var owner = new TestOwnerCoreObject();
        var item = new TestItemCoreObject { Title = "item1" };
        var list = new CoreList<TestItemCoreObject> { item };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);
        receivedOperations.Clear();

        // Act
        list.Remove(item);

        // Assert
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
        // Arrange
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

        // Act
        list.RemoveAt(1);

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (RemoveCollectionRangeOperation<TestItemCoreObject>)receivedOperations[0];
        Assert.Multiple(() =>
        {
            Assert.That(operation.Index, Is.EqualTo(1));
            Assert.That(operation.Items[0].Title, Is.EqualTo("item2"));
        });
    }

    [Test]
    public void Remove_CoreObjectItem_ShouldDisposeChildPublisher()
    {
        // Arrange
        var owner = new TestOwnerCoreObject();
        var item = new TestItemCoreObject();
        var list = new CoreList<TestItemCoreObject> { item };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        list.Remove(item);
        receivedOperations.Clear();

        // Act - modify removed item
        item.Title = "should not be tracked";

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void RemoveRange_ShouldPublishWithAllRemovedItems()
    {
        // Arrange
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

        // Act
        list.RemoveRange(1, 2);

        // Assert
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
        // Arrange
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

        // Act
        list.Clear();

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (RemoveCollectionRangeOperation<TestItemCoreObject>)receivedOperations[0];
        Assert.That(operation.Items, Has.Length.EqualTo(2));
    }

    [Test]
    public void Clear_WithDefaultResetBehavior_ShouldNotPublish()
    {
        // Arrange
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>
        {
            new() { Title = "item1" },
            new() { Title = "item2" }
        };
        // ResetBehavior defaults to Reset, which sends a Reset event not handled by observer
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);
        receivedOperations.Clear();

        // Act
        list.Clear();

        // Assert - Reset event is not handled by CollectionOperationObserver
        Assert.That(receivedOperations, Is.Empty);
    }

    #endregion

    #region Move Operation Tests (EnqueueMove)

    [Test]
    public void Move_ShouldPublishMoveCollectionRangeOperation()
    {
        // Arrange
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

        // Act
        list.Move(0, 2);

        // Assert
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
        // Arrange
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

        // Act - Move items at index 0-1 to index 4 (which becomes index 2 after removal adjustment)
        list.MoveRange(0, 2, 4);

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (MoveCollectionRangeOperation<TestItemCoreObject>)receivedOperations[0];
        // Note: CoreList adjusts newIndex when it's after oldIndex, so the event reports newIndex=2
        Assert.Multiple(() =>
        {
            Assert.That(operation.OldIndex, Is.EqualTo(0));
            Assert.That(operation.NewIndex, Is.EqualTo(2)); // Adjusted from 4 to 2 (4 - count)
            Assert.That(operation.Count, Is.EqualTo(2));
        });
    }

    #endregion

    #region Replace Operation Tests (EnqueueReplace)

    [Test]
    public void Replace_ShouldPublishRemoveAndInsertOperations()
    {
        // Arrange
        var owner = new TestOwnerCoreObject();
        var oldItem = new TestItemCoreObject { Title = "old" };
        var list = new CoreList<TestItemCoreObject> { oldItem };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);
        receivedOperations.Clear();

        // Act - Replace using indexer
        var newItem = new TestItemCoreObject { Title = "new" };
        list[0] = newItem;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(2));
        Assert.That(receivedOperations[0], Is.TypeOf<RemoveCollectionRangeOperation<TestItemCoreObject>>());
        Assert.That(receivedOperations[1], Is.TypeOf<InsertCollectionRangeOperation<TestItemCoreObject>>());
    }

    [Test]
    public void Replace_ShouldDisposeOldChildPublisherAndCreateNew()
    {
        // Arrange
        var owner = new TestOwnerCoreObject();
        var oldItem = new TestItemCoreObject();
        var list = new CoreList<TestItemCoreObject> { oldItem };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        var newItem = new TestItemCoreObject();
        list[0] = newItem; // Replace using indexer
        receivedOperations.Clear();

        // Act
        oldItem.Title = "old - not tracked";
        newItem.Title = "new - tracked";

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<string>)receivedOperations[0];
        Assert.That(operation.NewValue, Is.EqualTo("new - tracked"));
    }

    [Test]
    public void ReplaceAll_ShouldPublishRemoveAndInsertOperations()
    {
        // Arrange
        var owner = new TestOwnerCoreObject();
        var oldItem1 = new TestItemCoreObject { Title = "old1" };
        var oldItem2 = new TestItemCoreObject { Title = "old2" };
        var list = new CoreList<TestItemCoreObject> { oldItem1, oldItem2 };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);
        receivedOperations.Clear();

        // Act - Replace entire list
        var newItems = new List<TestItemCoreObject>
        {
            new() { Title = "new1" },
            new() { Title = "new2" },
            new() { Title = "new3" }
        };
        list.Replace(newItems);

        // Assert - Replace triggers a single Replace event
        Assert.That(receivedOperations, Has.Count.EqualTo(2));
        Assert.That(receivedOperations[0], Is.TypeOf<RemoveCollectionRangeOperation<TestItemCoreObject>>());
        Assert.That(receivedOperations[1], Is.TypeOf<InsertCollectionRangeOperation<TestItemCoreObject>>());
    }

    #endregion

    #region Dispose Tests

    [Test]
    public void Dispose_ShouldDisposeAllChildPublishers()
    {
        // Arrange
        var owner = new TestOwnerCoreObject();
        var item1 = new TestItemCoreObject();
        var item2 = new TestItemCoreObject();
        var list = new CoreList<TestItemCoreObject> { item1, item2 };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        // Act
        operationObserver.Dispose();
        item1.Title = "should not be tracked";
        item2.Title = "should not be tracked";

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Dispose_ShouldUnsubscribeFromCollectionChanges()
    {
        // Arrange
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        // Act
        operationObserver.Dispose();
        list.Add(new TestItemCoreObject());

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Dispose_ShouldCompleteOperationsObservable()
    {
        // Arrange
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>();
        var completed = false;
        var testObserver = Observer.Create<ChangeOperation>(_ => { });

        var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        operationObserver.Operations.Subscribe(_ => { }, () => completed = true);

        // Act
        operationObserver.Dispose();

        // Assert
        Assert.That(completed, Is.True);
    }

    #endregion

    #region PublishingSuppression Tests

    [Test]
    public void Add_WhenPublishingSuppressed_ShouldNotPublish()
    {
        // Arrange
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        // Act
        using (PublishingSuppression.Enter())
        {
            list.Add(new TestItemCoreObject());
        }

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Remove_WhenPublishingSuppressed_ShouldNotPublish()
    {
        // Arrange
        var owner = new TestOwnerCoreObject();
        var item = new TestItemCoreObject();
        var list = new CoreList<TestItemCoreObject> { item };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);
        receivedOperations.Clear();

        // Act
        using (PublishingSuppression.Enter())
        {
            list.Remove(item);
        }

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Move_WhenPublishingSuppressed_ShouldNotPublish()
    {
        // Arrange
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>
        {
            new(),
            new()
        };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);
        receivedOperations.Clear();

        // Act
        using (PublishingSuppression.Enter())
        {
            list.Move(0, 1);
        }

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Replace_WhenPublishingSuppressed_ShouldNotPublish()
    {
        // Arrange
        var owner = new TestOwnerCoreObject();
        var oldItem = new TestItemCoreObject();
        var list = new CoreList<TestItemCoreObject> { oldItem };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);
        receivedOperations.Clear();

        // Act
        using (PublishingSuppression.Enter())
        {
            list[0] = new TestItemCoreObject(); // Replace using indexer
        }

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Add_AfterSuppressionEnds_ShouldPublish()
    {
        // Arrange
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        // Act
        using (PublishingSuppression.Enter())
        {
            list.Add(new TestItemCoreObject());
        }
        list.Add(new TestItemCoreObject());

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    #endregion

    #region PropertyPathsToTrack Tests

    [Test]
    public void Constructor_WithPropertyPathsToTrack_ShouldFilterChildPublisherPaths()
    {
        // Arrange
        var owner = new TestOwnerCoreObject();
        var item = new TestItemCoreObject();
        var list = new CoreList<TestItemCoreObject> { item };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        var propertyPathsToTrack = new HashSet<string> { "Items.Title" };

        // Act
        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator, propertyPathsToTrack);

        item.Title = "tracked";
        item.Value = 100; // Not in paths to track

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<string>)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("Items.Title"));
    }

    [Test]
    public void AddedItem_WithPropertyPathsToTrack_ShouldUseFilteredChildPublisher()
    {
        // Arrange
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

        // Act
        item.Title = "tracked";
        item.Value = 100;

        // Assert - only Name change should be tracked
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        Assert.That(receivedOperations[0], Is.TypeOf<UpdatePropertyValueOperation<string>>());
    }

    #endregion

    #region Sequence Number Tests

    [Test]
    public void Operations_ShouldHaveIncrementingSequenceNumbers()
    {
        // Arrange
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        // Act
        list.Add(new TestItemCoreObject());
        list.Add(new TestItemCoreObject());
        list.Add(new TestItemCoreObject());

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(3));
        Assert.That(receivedOperations[1].SequenceNumber, Is.GreaterThan(receivedOperations[0].SequenceNumber));
        Assert.That(receivedOperations[2].SequenceNumber, Is.GreaterThan(receivedOperations[1].SequenceNumber));
    }

    #endregion

    #region Operations Property Tests

    [Test]
    public void Operations_ShouldReturnObservable()
    {
        // Arrange
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>();
        var testObserver = Observer.Create<ChangeOperation>(_ => { });

        // Act
        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        // Assert
        Assert.That(operationObserver.Operations, Is.InstanceOf<IObservable<ChangeOperation>>());
    }

    [Test]
    public void Operations_ShouldAllowMultipleSubscribers()
    {
        // Arrange
        var owner = new TestOwnerCoreObject();
        var list = new CoreList<TestItemCoreObject>();
        var receivedOperations1 = new List<ChangeOperation>();
        var receivedOperations2 = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(_ => { });

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        // Act
        using var sub1 = operationObserver.Operations.Subscribe(op => receivedOperations1.Add(op));
        using var sub2 = operationObserver.Operations.Subscribe(op => receivedOperations2.Add(op));

        list.Add(new TestItemCoreObject());

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(receivedOperations1, Has.Count.EqualTo(1));
            Assert.That(receivedOperations2, Has.Count.EqualTo(1));
        });
    }

    #endregion

    #region Non-INotifyCollectionChanged List Tests

    [Test]
    public void Constructor_WithNonNotifyingList_ShouldNotSubscribeToChanges()
    {
        // Arrange
        var owner = new TestOwnerCoreObject();
        var list = new List<TestItemCoreObject>(); // Plain List, not CoreList
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CollectionOperationObserver<TestItemCoreObject>(
            testObserver, list, owner, "Items", _sequenceGenerator);

        // Act - add to list (won't trigger CollectionChanged since List<T> doesn't implement it)
        list.Add(new TestItemCoreObject());

        // Assert
        Assert.That(receivedOperations, Is.Empty);
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

    #endregion
}
