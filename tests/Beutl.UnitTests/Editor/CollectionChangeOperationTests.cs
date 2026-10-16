using Beutl.Collections;
using Beutl.Editor;
using Beutl.Editor.Operations;
using Beutl.Engine;
using Beutl.NodeGraph;

namespace Beutl.UnitTests.Editor;

public class CollectionChangeOperationTests
{

    [SetUp]
    public void Setup()
    {
        var root = new TestCoreObjectWithList();
    }

    #region Failure Classification Tests

    [Test]
    public void CompletedValueMove_DoesNotUseCoarseValueEqualityToClassifyFailure()
    {
        var owner = new TestValueListOwner();
        owner.Items.AddRange([new EqualValue(2), new EqualValue(1)]);
        var operation = new MoveCollectionRangeOperation<EqualValue>
        {
            Object = owner,
            PropertyPath = "Items",
            OldIndex = 0,
            NewIndex = 1,
            Count = 1,
            SequenceNumber = 1,
        };
        using var history = new Beutl.Editor.HistoryManager(new Beutl.Editor.OperationSequenceGenerator());
        history.Record(operation);
        history.Commit("value move");
        System.Collections.Specialized.NotifyCollectionChangedEventHandler handler = (_, change) =>
        {
            if (change.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                throw new IOException("observer");
        };
        owner.Items.CollectionChanged += handler;
        Assert.Throws<IOException>(() => history.Undo());
        Assert.That(history.Undo(), Is.True);
        Assert.That(owner.Items.Select(value => value.Id), Is.EqualTo(new[] { 1, 2 }));
        owner.Items.CollectionChanged -= handler;
    }

    private readonly struct EqualValue(int id) : IEquatable<EqualValue>
    {
        public int Id => id;
        public bool Equals(EqualValue other) => true;
        public override bool Equals(object? obj) => obj is EqualValue;
        public override int GetHashCode() => 0;
    }

    private sealed class TestValueListOwner : CoreObject
    {
        private static readonly CoreProperty<CoreList<EqualValue>> s_itemsProperty =
            ConfigureProperty<CoreList<EqualValue>, TestValueListOwner>(nameof(Items))
                .Accessor(owner => owner.Items, (owner, value) => owner.Items = value).Register();
        private CoreList<EqualValue> _items = [];
        public CoreList<EqualValue> Items
        {
            get => _items;
            set => SetAndRaise(s_itemsProperty, ref _items, value);
        }
    }

    [TestCase(1)]
    [TestCase(2)]
    public void StaleMoveDestination_DoesNotMutateTheCollectionWhenRetried(int count)
    {
        var owner = new TestCoreObjectWithList();
        owner.Items.AddRange(["A", "B", "C"]);
        var operation = new MoveCollectionRangeOperation<string> { Object = owner, PropertyPath = "Items", OldIndex = 5, NewIndex = 0, Count = count, SequenceNumber = 1 };
        using var history = new Beutl.Editor.HistoryManager(new Beutl.Editor.OperationSequenceGenerator());
        history.Record(operation);
        history.Commit("stale move");
        for (int i = 0; i < 2; i++)
        {
            Assert.Catch<ArgumentOutOfRangeException>(() => history.Undo());
            Assert.That(owner.Items, Is.EqualTo(new[] { "A", "B", "C" }));
        }
    }

    [Test]
    public void CompletedMoveWithThrowingObserver_IsNotRepeatedOnRetry()
    {
        var owner = new TestCoreObjectWithList();
        owner.Items.AddRange(["B", "C", "A"]);
        var operation = new MoveCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            OldIndex = 0,
            NewIndex = 2,
            Count = 1,
            SequenceNumber = 1,
        };
        using var history = new Beutl.Editor.HistoryManager(new Beutl.Editor.OperationSequenceGenerator());
        history.Record(operation);
        history.Commit("move");
        System.Collections.Specialized.NotifyCollectionChangedEventHandler handler = (_, change) =>
        {
            if (change.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                throw new IOException("observer failure after insertion");
        };
        owner.Items.CollectionChanged += handler;
        Assert.Throws<IOException>(() => history.Undo());
        Assert.That(owner.Items, Is.EqualTo(new[] { "A", "B", "C" }));
        Assert.That(history.Undo(), Is.True);
        Assert.That(owner.Items, Is.EqualTo(new[] { "A", "B", "C" }));
        owner.Items.CollectionChanged -= handler;
        Assert.That(history.Redo(), Is.True);
        Assert.That(owner.Items, Is.EqualTo(new[] { "B", "C", "A" }));
    }

    #endregion

    #region InsertCollectionRangeOperation Tests

    [TestCaseSource(nameof(ListOwners))]
    public void InsertCollectionRangeOperation_ApplyTo_ShouldInsertItemsAtIndex(CreateListOwner create)
    {
        (CoreObject owner, string propertyPath, IList<string> list) = create(["A", "D"]);

        var operation = new InsertCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = propertyPath,
            Items = ["B", "C"],
            Index = 1,
            SequenceNumber = 1
        };

        operation.Apply();

        Assert.That(list, Is.EqualTo(new[] { "A", "B", "C", "D" }));
    }

    [TestCaseSource(nameof(ListOwners))]
    public void InsertCollectionRangeOperation_RevertTo_ShouldRemoveItemsAtIndex(CreateListOwner create)
    {
        (CoreObject owner, string propertyPath, IList<string> list) = create(["A", "B", "C", "D"]);

        var operation = new InsertCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = propertyPath,
            Items = ["B", "C"],
            Index = 1,
            SequenceNumber = 1
        };

        operation.Revert();

        Assert.That(list, Is.EqualTo(new[] { "A", "D" }));
    }

    [Test]
    public void InsertCollectionRangeOperation_ApplyTo_WhenIndexNegative_ShouldAddToEnd()
    {
        var owner = new TestCoreObjectWithList();
        owner.Items.AddRange(["A", "B"]);

        var operation = new InsertCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Items = ["X", "Y"],
            Index = -1,
            SequenceNumber = 1
        };

        operation.Apply();

        Assert.That(owner.Items, Is.EqualTo(new[] { "A", "B", "X", "Y" }));
    }

    [Test]
    public void InsertCollectionRangeOperation_ApplyTo_WhenIndexGreaterThanCount_ShouldAddToEnd()
    {
        var owner = new TestCoreObjectWithList();
        owner.Items.AddRange(["A", "B"]);

        var operation = new InsertCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Items = ["X", "Y"],
            Index = 100,
            SequenceNumber = 1
        };

        operation.Apply();

        Assert.That(owner.Items, Is.EqualTo(new[] { "A", "B", "X", "Y" }));
    }

    [Test]
    public void InsertCollectionRangeOperation_Items_ShouldReturnAllItems()
    {
        var owner = new TestCoreObjectWithList();
        var items = new[] { "X", "Y", "Z" };
        var operation = new InsertCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Items = items,
            Index = 0,
            SequenceNumber = 1
        };

        var opItems = ((ICollectionChangeOperation)operation).Items;

        Assert.That(opItems, Is.EqualTo(items.Cast<object?>()));
    }

    #endregion

    #region RemoveCollectionRangeOperation Tests

    [TestCaseSource(nameof(ListOwners))]
    public void RemoveCollectionRangeOperation_ApplyTo_ShouldRemoveItemsAtIndex(CreateListOwner create)
    {
        (CoreObject owner, string propertyPath, IList<string> list) = create(["A", "B", "C", "D"]);

        var operation = new RemoveCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = propertyPath,
            Items = ["B", "C"],
            Index = 1,
            SequenceNumber = 1
        };

        operation.Apply();

        Assert.That(list, Is.EqualTo(new[] { "A", "D" }));
    }

    [TestCaseSource(nameof(ListOwners))]
    public void RemoveCollectionRangeOperation_RevertTo_ShouldInsertItemsAtIndex(CreateListOwner create)
    {
        (CoreObject owner, string propertyPath, IList<string> list) = create(["A", "D"]);

        var operation = new RemoveCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = propertyPath,
            Items = ["B", "C"],
            Index = 1,
            SequenceNumber = 1
        };

        operation.Revert();

        Assert.That(list, Is.EqualTo(new[] { "A", "B", "C", "D" }));
    }

    [Test]
    public void RemoveCollectionRangeOperation_Items_ShouldReturnAllItems()
    {
        var owner = new TestCoreObjectWithList();
        var items = new[] { "X", "Y", "Z" };
        var operation = new RemoveCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Items = items,
            Index = 0,
            SequenceNumber = 1
        };

        var opItems = ((ICollectionChangeOperation)operation).Items;

        Assert.That(opItems, Is.EqualTo(items.Cast<object?>()));
    }

    #endregion

    #region MoveCollectionRangeOperation Tests

    [TestCaseSource(nameof(ListOwners))]
    public void MoveCollectionRangeOperation_ApplyTo_ShouldMoveItems(CreateListOwner create)
    {
        (CoreObject owner, string propertyPath, IList<string> list) = create(["A", "B", "C", "D", "E"]);

        var operation = new MoveCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = propertyPath,
            OldIndex = 0,
            NewIndex = 3,
            Count = 2,
            SequenceNumber = 1
        };

        operation.Apply();

        Assert.That(list, Is.EqualTo(new[] { "C", "D", "E", "A", "B" }));
    }

    [TestCaseSource(nameof(ListOwners))]
    public void MoveCollectionRangeOperation_RevertTo_ShouldMoveItemsBack(CreateListOwner create)
    {
        (CoreObject owner, string propertyPath, IList<string> list) = create(["C", "D", "E", "A", "B"]);

        var operation = new MoveCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = propertyPath,
            OldIndex = 0,
            NewIndex = 3,
            Count = 2,
            SequenceNumber = 1
        };

        operation.Revert();

        Assert.That(list, Is.EqualTo(new[] { "A", "B", "C", "D", "E" }));
    }

    [Test]
    public void MoveCollectionRangeOperation_ApplyTo_WhenMovingBackward_ShouldMoveItems()
    {
        var owner = new TestCoreObjectWithList();
        owner.Items.AddRange(["A", "B", "C", "D", "E"]);

        var operation = new MoveCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            OldIndex = 3,
            NewIndex = 1,
            Count = 2,
            SequenceNumber = 1
        };

        operation.Apply();

        Assert.That(owner.Items, Is.EqualTo(new[] { "A", "D", "E", "B", "C" }));
    }

    [Test]
    public void MoveCollectionRangeOperation_Items_ShouldReturnEmptyCollection()
    {
        var owner = new TestCoreObjectWithList();
        var operation = new MoveCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            OldIndex = 0,
            NewIndex = 3,
            Count = 2,
            SequenceNumber = 1
        };

        var items = ((ICollectionChangeOperation)operation).Items;

        Assert.That(items, Is.Empty);
    }

    #endregion

    #region CollectionChangeOperation Base Class Tests

    [Test]
    public void CollectionChangeOperation_Apply_WhenPropertyNotList_ShouldThrowInvalidOperationException()
    {
        var owner = new TestCoreObjectWithScalarProperty();

        var operation = new InsertCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Title",
            Items = ["X"],
            Index = 0,
            SequenceNumber = 1
        };

        Assert.Throws<InvalidOperationException>(() => operation.Apply());
    }

    [Test]
    public void CollectionChangeOperation_Apply_WhenPropertyNotFound_ShouldNotThrow()
    {
        // A plain CoreObject without the property is silently skipped.
        var owner = new TestCoreObjectWithList();

        var operation = new InsertCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "NonExistentProperty",
            Items = ["X"],
            Index = 0,
            SequenceNumber = 1
        };

        Assert.DoesNotThrow(() => operation.Apply());
    }

    [Test]
    public void CollectionChangeOperation_Revert_WhenPropertyNotList_ShouldThrowInvalidOperationException()
    {
        var owner = new TestCoreObjectWithScalarProperty();

        var operation = new InsertCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Title",
            Items = ["X"],
            Index = 0,
            SequenceNumber = 1
        };

        Assert.Throws<InvalidOperationException>(() => operation.Revert());
    }

    [Test]
    public void CollectionChangeOperation_Apply_WhenEnginePropertyNotList_ShouldThrowInvalidOperationException()
    {
        var owner = new TestEngineObjectWithScalarProperty();

        var operation = new InsertCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Title",
            Items = ["X"],
            Index = 0,
            SequenceNumber = 1
        };

        Assert.Throws<InvalidOperationException>(() => operation.Apply());
    }

    [Test]
    public void CollectionChangeOperation_Apply_WhenEnginePropertyNotFound_ShouldThrowInvalidOperationException()
    {
        var owner = new TestEngineObjectWithList();

        var operation = new InsertCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "NonExistentProperty",
            Items = ["X"],
            Index = 0,
            SequenceNumber = 1
        };

        Assert.Throws<InvalidOperationException>(() => operation.Apply());
    }

    [Test]
    public void CollectionChangeOperation_PropertyPath_ShouldBeAccessible()
    {
        var owner = new TestCoreObjectWithList();
        var operation = new InsertCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Items = ["X"],
            Index = 0,
            SequenceNumber = 1
        };

        var propertyPath = ((IPropertyPathProvider)operation).PropertyPath;

        Assert.That(propertyPath, Is.EqualTo("Items"));
    }

    #endregion

    #region NodeMember Tests

    [Test]
    public void CollectionChangeOperation_Apply_WhenNodeMemberPropertyIsNull_ShouldThrowInvalidOperationException()
    {
        var nodeMember = new DefaultNodeMember<CoreList<string>>();

        var operation = new InsertCollectionRangeOperation<string>
        {
            Object = nodeMember,
            PropertyPath = "Property",
            Items = ["X"],
            Index = 0,
            SequenceNumber = 1
        };

        Assert.Throws<InvalidOperationException>(() => operation.Apply());
    }

    [Test]
    public void CollectionChangeOperation_Apply_WhenNodeMemberPropertyValueIsNull_ShouldThrowInvalidOperationException()
    {
        var adapter = new NodePropertyAdapter<CoreList<string>>("Property", null, null);
        var nodeMember = new DefaultNodeMember<CoreList<string>>();
        nodeMember.SetProperty(adapter);

        var operation = new InsertCollectionRangeOperation<string>
        {
            Object = nodeMember,
            PropertyPath = "Property",
            Items = ["X"],
            Index = 0,
            SequenceNumber = 1
        };

        Assert.Throws<InvalidOperationException>(() => operation.Apply());
    }

    [Test]
    public void CollectionChangeOperation_Apply_WhenNodeMemberPropertyPathNotProperty_ShouldInvalidOperationException()
    {
        // A NodeMember with PropertyPath != "Property" falls through to the EngineObject lookup, which throws
        var list = new CoreList<string>(["A", "B", "C"]);
        var adapter = new NodePropertyAdapter<CoreList<string>>("Property", list, null);
        var nodeMember = new DefaultNodeMember<CoreList<string>>();
        nodeMember.SetProperty(adapter);

        var operation = new InsertCollectionRangeOperation<string>
        {
            Object = nodeMember,
            PropertyPath = "OtherProperty",
            Items = ["X"],
            Index = 0,
            SequenceNumber = 1
        };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => operation.Apply());
    }

    #endregion

    #region Test Helper Classes

    public delegate (CoreObject Owner, string PropertyPath, IList<string> List) CreateListOwner(string[] items);

    // CollectionChangeOperation reaches the list in three ways: a CoreList in a CoreProperty, a
    // ListProperty of an EngineObject, and the NodePropertyAdapter of a DefaultNodeMember.
    private static IEnumerable<TestCaseData> ListOwners()
    {
        yield return new TestCaseData(new CreateListOwner(items =>
        {
            var owner = new TestCoreObjectWithList();
            owner.Items.AddRange(items);
            return (owner, "Items", owner.Items);
        })).SetArgDisplayNames("CoreObject");

        yield return new TestCaseData(new CreateListOwner(items =>
        {
            var owner = new TestEngineObjectWithList();
            foreach (string item in items)
            {
                owner.Items.Add(item);
            }

            return (owner, "Items", owner.Items);
        })).SetArgDisplayNames("EngineObject");

        yield return new TestCaseData(new CreateListOwner(items =>
        {
            var list = new CoreList<string>(items);
            var owner = new DefaultNodeMember<CoreList<string>>();
            owner.SetProperty(new NodePropertyAdapter<CoreList<string>>("Property", list, null));
            return (owner, "Property", list);
        })).SetArgDisplayNames("NodeMember");
    }

    private class TestCoreObjectWithList : CoreObject
    {
        public static readonly CoreProperty<CoreList<string>> ItemsProperty;

        private CoreList<string> _items = [];

        static TestCoreObjectWithList()
        {
            ItemsProperty = ConfigureProperty<CoreList<string>, TestCoreObjectWithList>(nameof(Items))
                .Accessor(o => o.Items, (o, v) => o.Items = v)
                .Register();
        }

        public CoreList<string> Items
        {
            get => _items;
            set => SetAndRaise(ItemsProperty, ref _items, value);
        }
    }

    private class TestCoreObjectWithScalarProperty : CoreObject
    {
        public static readonly CoreProperty<string> TitleProperty;

        private string _title = "";

        static TestCoreObjectWithScalarProperty()
        {
            TitleProperty = ConfigureProperty<string, TestCoreObjectWithScalarProperty>(nameof(Title))
                .Accessor(o => o.Title, (o, v) => o.Title = v)
                .Register();
        }

        public string Title
        {
            get => _title;
            set => SetAndRaise(TitleProperty, ref _title, value);
        }
    }

    [SuppressResourceClassGeneration]
    private class TestEngineObjectWithList : EngineObject
    {
        public TestEngineObjectWithList()
        {
            Items = new ListProperty<string>();
            ScanProperties<TestEngineObjectWithList>();
        }

        public ListProperty<string> Items { get; }
    }

    [SuppressResourceClassGeneration]
    private class TestEngineObjectWithScalarProperty : EngineObject
    {
        public TestEngineObjectWithScalarProperty()
        {
            Title = Property.Create("");
            ScanProperties<TestEngineObjectWithScalarProperty>();
        }

        public IProperty<string> Title { get; }
    }

    #endregion
}
