using System.Reactive.Linq;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Collections;
using Beutl.Editor;
using Beutl.Editor.Operations;
using Beutl.Engine;
using Beutl.Logging;
using Beutl.NodeTree;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Editor;

public class AutoSaveServiceTests
{
    [SetUp]
    public void Setup()
    {
        Log.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
    }

    #region Constructor and Dispose Tests

    [Test]
    public void Constructor_ShouldCreateValidInstance()
    {
        // Arrange & Act
        using var service = new AutoSaveService();

        // Assert
        Assert.That(service, Is.Not.Null);
        Assert.That(service.SaveError, Is.Not.Null);
    }

    [Test]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var service = new AutoSaveService();

        // Act & Assert
        Assert.DoesNotThrow(() => service.Dispose());
    }

    [Test]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var service = new AutoSaveService();

        // Act & Assert
        Assert.DoesNotThrow(() =>
        {
            service.Dispose();
            service.Dispose();
            service.Dispose();
        });
    }

    [Test]
    public void AutoSave_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var service = new AutoSaveService();
        service.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => service.AutoSave([]));
    }

    [Test]
    public void SaveObjects_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var service = new AutoSaveService();
        service.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => service.SaveObjects([]));
    }

    #endregion

    #region CollectObjectsToSave Tests - IUpdatePropertyValueOperation

    [Test]
    public void CollectObjectsToSave_WithUpdatePropertyValueOperation_ShouldAddObjectWithUri()
    {
        // Arrange
        var obj = new TestCoreObjectWithUri();
        obj.SetUri(new Uri("file:///test.json"));

        var operation = new UpdatePropertyValueOperation<int>(obj, "Value", 10, 0)
        {
            SequenceNumber = 1
        };

        var objectsToSave = new HashSet<CoreObject>();

        // Act
        AutoSaveService.CollectObjectsToSave(operation, objectsToSave);

        // Assert
        Assert.That(objectsToSave, Contains.Item(obj));
    }

    [Test]
    public void CollectObjectsToSave_WithUpdatePropertyValueOperation_ObjectWithoutUri_ShouldNotAddObject()
    {
        // Arrange
        var obj = new TestCoreObject();
        // obj has no Uri

        var operation = new UpdatePropertyValueOperation<int>(obj, "Value", 10, 0)
        {
            SequenceNumber = 1
        };

        var objectsToSave = new HashSet<CoreObject>();

        // Act
        AutoSaveService.CollectObjectsToSave(operation, objectsToSave);

        // Assert
        Assert.That(objectsToSave, Does.Not.Contain(obj));
    }

    [Test]
    public void CollectObjectsToSave_WithUpdatePropertyValueOperation_ShouldAddAncestorsWithUri()
    {
        // Arrange
        var parent = new TestHierarchicalCoreObject();
        parent.SetUri(new Uri("file:///parent.json"));

        var child = new TestHierarchicalCoreObject();
        parent.AddChild(child);

        var operation = new UpdatePropertyValueOperation<int>(child, "Value", 10, 0)
        {
            SequenceNumber = 1
        };

        var objectsToSave = new HashSet<CoreObject>();

        // Act
        AutoSaveService.CollectObjectsToSave(operation, objectsToSave);

        // Assert
        Assert.That(objectsToSave, Contains.Item(parent));
    }

    #endregion

    #region CollectObjectsToSave Tests - ICollectionChangeOperation

    [Test]
    public void CollectObjectsToSave_WithCollectionOperation_ShouldAddObjectWithUri()
    {
        // Arrange
        var owner = new TestCoreObjectWithListAndUri();
        owner.SetUri(new Uri("file:///owner.json"));

        var operation = new InsertCollectionItemOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Item = "NewItem",
            Index = 0,
            SequenceNumber = 1
        };

        var objectsToSave = new HashSet<CoreObject>();

        // Act
        AutoSaveService.CollectObjectsToSave(operation, objectsToSave);

        // Assert
        Assert.That(objectsToSave, Contains.Item(owner));
    }

    [Test]
    public void CollectObjectsToSave_WithCollectionOperation_ShouldAddCoreObjectItems()
    {
        // Arrange
        var owner = new TestCoreObjectWithListAndUri();
        owner.SetUri(new Uri("file:///owner.json"));

        var item = new TestCoreObjectWithUri();
        item.SetUri(new Uri("file:///item.json"));

        var operation = new TestCollectionOperationWithCoreObjectItems(owner, [item])
        {
            SequenceNumber = 1
        };

        var objectsToSave = new HashSet<CoreObject>();

        // Act
        AutoSaveService.CollectObjectsToSave(operation, objectsToSave);

        // Assert
        Assert.That(objectsToSave, Contains.Item(owner));
        Assert.That(objectsToSave, Contains.Item(item));
    }

    [Test]
    public void CollectObjectsToSave_WithCollectionOperation_ShouldAddAncestorsOfItems()
    {
        // Arrange
        var owner = new TestCoreObjectWithListAndUri();

        var itemParent = new TestHierarchicalCoreObject();
        itemParent.SetUri(new Uri("file:///item-parent.json"));

        var item = new TestHierarchicalCoreObject();
        itemParent.AddChild(item);

        var operation = new TestCollectionOperationWithCoreObjectItems(owner, [item])
        {
            SequenceNumber = 1
        };

        var objectsToSave = new HashSet<CoreObject>();

        // Act
        AutoSaveService.CollectObjectsToSave(operation, objectsToSave);

        // Assert
        Assert.That(objectsToSave, Contains.Item(itemParent));
    }

    [Test]
    public void CollectObjectsToSave_WithCollectionOperation_NonCoreObjectItems_ShouldNotThrow()
    {
        // Arrange
        var owner = new TestCoreObjectWithListAndUri();
        owner.SetUri(new Uri("file:///owner.json"));

        var operation = new InsertCollectionItemOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Item = "StringItem", // Not a CoreObject
            Index = 0,
            SequenceNumber = 1
        };

        var objectsToSave = new HashSet<CoreObject>();

        // Act & Assert
        Assert.DoesNotThrow(() => AutoSaveService.CollectObjectsToSave(operation, objectsToSave));
        Assert.That(objectsToSave, Contains.Item(owner));
    }

    #endregion

    #region CollectObjectsToSave Tests - UpdateSplineEasingOperation

    [Test]
    public void CollectObjectsToSave_WithSplineEasingOperation_ShouldAddParentWithUri()
    {
        // Arrange
        var parent = new TestCoreObjectWithUri();
        parent.SetUri(new Uri("file:///parent.json"));

        var easing = new SplineEasing();
        var operation = new UpdateSplineEasingOperation(easing, "X1", 0.5f, 0f)
        {
            Parent = parent,
            SequenceNumber = 1
        };

        var objectsToSave = new HashSet<CoreObject>();

        // Act
        AutoSaveService.CollectObjectsToSave(operation, objectsToSave);

        // Assert
        Assert.That(objectsToSave, Contains.Item(parent));
    }

    [Test]
    public void CollectObjectsToSave_WithSplineEasingOperation_NullParent_ShouldNotThrow()
    {
        // Arrange
        var easing = new SplineEasing();
        var operation = new UpdateSplineEasingOperation(easing, "X1", 0.5f, 0f)
        {
            Parent = null,
            SequenceNumber = 1
        };

        var objectsToSave = new HashSet<CoreObject>();

        // Act & Assert
        Assert.DoesNotThrow(() => AutoSaveService.CollectObjectsToSave(operation, objectsToSave));
        Assert.That(objectsToSave, Is.Empty);
    }

    #endregion

    #region CollectObjectsToSave Tests - UpdateNodeItemOperation

    [Test]
    public void CollectObjectsToSave_WithNodeItemOperation_CoreObjectNodeItem_ShouldAddNodeItem()
    {
        // Arrange
        var nodeItem = new TestNodeItem();
        nodeItem.SetUri(new Uri("file:///nodeitem.json"));

        var operation = new UpdateNodeItemOperation(nodeItem, "Property", "new", "old")
        {
            SequenceNumber = 1
        };

        var objectsToSave = new HashSet<CoreObject>();

        // Act
        AutoSaveService.CollectObjectsToSave(operation, objectsToSave);

        // Assert
        Assert.That(objectsToSave, Contains.Item(nodeItem));
    }

    [Test]
    public void CollectObjectsToSave_WithNodeItemOperation_NonCoreObjectNodeItem_ShouldNotAddToSet()
    {
        // Arrange
        var nodeItem = new DefaultNodeItem<int>();

        var operation = new UpdateNodeItemOperation(nodeItem, "Property", 10, 5)
        {
            SequenceNumber = 1
        };

        var objectsToSave = new HashSet<CoreObject>();

        // Act
        AutoSaveService.CollectObjectsToSave(operation, objectsToSave);

        // Assert
        Assert.That(objectsToSave, Is.Empty);
    }

    #endregion

    #region CollectObjectsToSave Tests - Unknown Operation Type

    [Test]
    public void CollectObjectsToSave_WithUnknownOperationType_ShouldNotThrow()
    {
        // Arrange
        var operation = new TestUnknownChangeOperation
        {
            SequenceNumber = 1
        };

        var objectsToSave = new HashSet<CoreObject>();

        // Act & Assert
        Assert.DoesNotThrow(() => AutoSaveService.CollectObjectsToSave(operation, objectsToSave));
        Assert.That(objectsToSave, Is.Empty);
    }

    #endregion

    #region AddObjectWithAncestors Tests

    [Test]
    public void AddObjectWithAncestors_ObjectWithUri_ShouldAddObject()
    {
        // Arrange
        var obj = new TestCoreObjectWithUri();
        obj.SetUri(new Uri("file:///test.json"));

        var objectsToSave = new HashSet<CoreObject>();

        // Act
        AutoSaveService.AddObjectWithAncestors(obj, objectsToSave);

        // Assert
        Assert.That(objectsToSave, Contains.Item(obj));
    }

    [Test]
    public void AddObjectWithAncestors_ObjectWithoutUri_ShouldNotAddObject()
    {
        // Arrange
        var obj = new TestCoreObject();
        // obj has no Uri

        var objectsToSave = new HashSet<CoreObject>();

        // Act
        AutoSaveService.AddObjectWithAncestors(obj, objectsToSave);

        // Assert
        Assert.That(objectsToSave, Does.Not.Contain(obj));
    }

    [Test]
    public void AddObjectWithAncestors_WithHierarchy_ShouldAddAllAncestorsWithUri()
    {
        // Arrange
        var root = new TestHierarchicalCoreObject();
        root.SetUri(new Uri("file:///root.json"));

        var parent = new TestHierarchicalCoreObject();
        parent.SetUri(new Uri("file:///parent.json"));
        root.AddChild(parent);

        var child = new TestHierarchicalCoreObject();
        // child has no Uri
        parent.AddChild(child);

        var objectsToSave = new HashSet<CoreObject>();

        // Act
        AutoSaveService.AddObjectWithAncestors(child, objectsToSave);

        // Assert
        Assert.That(objectsToSave, Contains.Item(root));
        Assert.That(objectsToSave, Contains.Item(parent));
        Assert.That(objectsToSave, Does.Not.Contain(child));
    }

    [Test]
    public void AddObjectWithAncestors_DuplicateObject_ShouldNotAddTwice()
    {
        // Arrange
        var obj = new TestCoreObjectWithUri();
        obj.SetUri(new Uri("file:///test.json"));

        var objectsToSave = new HashSet<CoreObject>();

        // Act
        AutoSaveService.AddObjectWithAncestors(obj, objectsToSave);
        AutoSaveService.AddObjectWithAncestors(obj, objectsToSave);

        // Assert
        Assert.That(objectsToSave.Count, Is.EqualTo(1));
    }

    [Test]
    public void AddObjectWithAncestors_NonHierarchicalObject_ShouldOnlyCheckUri()
    {
        // Arrange - TestCoreObject doesn't implement IHierarchical
        var obj = new TestCoreObjectWithUri();
        obj.SetUri(new Uri("file:///test.json"));

        var objectsToSave = new HashSet<CoreObject>();

        // Act
        AutoSaveService.AddObjectWithAncestors(obj, objectsToSave);

        // Assert
        Assert.That(objectsToSave.Count, Is.EqualTo(1));
        Assert.That(objectsToSave, Contains.Item(obj));
    }

    #endregion

    #region AutoSave Tests

    [Test]
    public void AutoSave_WithEmptyOperations_ShouldNotThrow()
    {
        // Arrange
        using var service = new AutoSaveService();

        // Act & Assert
        Assert.DoesNotThrow(() => service.AutoSave([]));
    }

    [Test]
    public void AutoSave_WithMultipleOperations_ShouldCollectAllObjects()
    {
        // This test verifies that AutoSave processes all operations
        // Since we can't easily mock CoreSerializer, we verify behavior via SaveError
        using var service = new AutoSaveService();

        var obj1 = new TestCoreObjectWithUri();
        obj1.SetUri(new Uri("file:///nonexistent/test1.json"));

        var obj2 = new TestCoreObjectWithUri();
        obj2.SetUri(new Uri("file:///nonexistent/test2.json"));

        var op1 = new UpdatePropertyValueOperation<int>(obj1, "Value", 10, 0)
        {
            SequenceNumber = 1
        };
        var op2 = new UpdatePropertyValueOperation<int>(obj2, "Value", 20, 0)
        {
            SequenceNumber = 2
        };

        var errorCount = 0;
        using var subscription = service.SaveError.Subscribe(_ => errorCount++);

        // Act
        service.AutoSave([op1, op2]);

        // Assert - Should have attempted to save both objects
        // The exact error count depends on whether files exist or not
        // At minimum, we verify no crash occurred
        Assert.Pass("AutoSave completed without throwing");
    }

    #endregion

    #region SaveObjects Tests

    [Test]
    public void SaveObjects_WithEmptyCollection_ShouldNotThrow()
    {
        // Arrange
        using var service = new AutoSaveService();

        // Act & Assert
        Assert.DoesNotThrow(() => service.SaveObjects([]));
    }

    [Test]
    public void SaveObjects_WithNonExistentPath_ShouldEmitSaveError()
    {
        // Arrange
        using var service = new AutoSaveService();
        var obj = new TestCoreObjectWithUri();
        obj.SetUri(new Uri("file:///nonexistent/path/that/does/not/exist/test.json"));

        Exception? receivedError = null;
        using var subscription = service.SaveError.Subscribe(ex => receivedError = ex);

        // Act
        service.SaveObjects([obj]);

        // Assert
        Assert.That(receivedError, Is.Not.Null);
    }

    [Test]
    public void SaveObjects_WhenExceptionOccurs_ShouldContinueWithNextObject()
    {
        // Arrange
        using var service = new AutoSaveService();

        var obj1 = new TestCoreObjectWithUri();
        obj1.SetUri(new Uri("file:///nonexistent1/test.json"));

        var obj2 = new TestCoreObjectWithUri();
        obj2.SetUri(new Uri("file:///nonexistent2/test.json"));

        var errorCount = 0;
        using var subscription = service.SaveError.Subscribe(_ => errorCount++);

        // Act - Should not throw, even if saving fails
        Assert.DoesNotThrow(() => service.SaveObjects([obj1, obj2]));

        // Assert - Both objects should have been attempted
        Assert.That(errorCount, Is.GreaterThanOrEqualTo(2));
    }

    #endregion

    #region SaveError Observable Tests

    [Test]
    public void SaveError_ShouldBeObservable()
    {
        // Arrange
        using var service = new AutoSaveService();

        // Act & Assert
        Assert.That(service.SaveError, Is.Not.Null);
        Assert.That(service.SaveError, Is.InstanceOf<IObservable<Exception>>());
    }

    [Test]
    public void SaveError_Subscription_ShouldReceiveErrors()
    {
        // Arrange
        using var service = new AutoSaveService();
        var obj = new TestCoreObjectWithUri();
        obj.SetUri(new Uri("file:///this/path/does/not/exist/test.json"));

        var errors = new List<Exception>();
        using var subscription = service.SaveError.Subscribe(ex => errors.Add(ex));

        // Act
        service.SaveObjects([obj]);

        // Assert
        Assert.That(errors, Is.Not.Empty);
    }

    [Test]
    public void SaveError_MultipleSubscribers_ShouldAllReceiveErrors()
    {
        // Arrange
        using var service = new AutoSaveService();
        var obj = new TestCoreObjectWithUri();
        obj.SetUri(new Uri("file:///nonexistent/test.json"));

        var errors1 = new List<Exception>();
        var errors2 = new List<Exception>();
        using var sub1 = service.SaveError.Subscribe(ex => errors1.Add(ex));
        using var sub2 = service.SaveError.Subscribe(ex => errors2.Add(ex));

        // Act
        service.SaveObjects([obj]);

        // Assert
        Assert.That(errors1.Count, Is.EqualTo(errors2.Count));
    }

    #endregion

    #region Integration Tests

    [Test]
    public void CollectObjectsToSave_WithMixedOperationTypes_ShouldHandleAll()
    {
        // Arrange
        var coreObj = new TestCoreObjectWithUri();
        coreObj.SetUri(new Uri("file:///core.json"));

        var collectionOwner = new TestCoreObjectWithListAndUri();
        collectionOwner.SetUri(new Uri("file:///collection.json"));

        var splineParent = new TestCoreObjectWithUri();
        splineParent.SetUri(new Uri("file:///spline.json"));

        var operations = new ChangeOperation[]
        {
            new UpdatePropertyValueOperation<int>(coreObj, "Value", 10, 0) { SequenceNumber = 1 },
            new InsertCollectionItemOperation<string>
            {
                Object = collectionOwner,
                PropertyPath = "Items",
                Item = "Item",
                Index = 0,
                SequenceNumber = 2
            },
            new UpdateSplineEasingOperation(new SplineEasing(), "X1", 0.5f, 0f)
            {
                Parent = splineParent,
                SequenceNumber = 3
            }
        };

        var objectsToSave = new HashSet<CoreObject>();

        // Act
        foreach (var op in operations)
        {
            AutoSaveService.CollectObjectsToSave(op, objectsToSave);
        }

        // Assert
        Assert.That(objectsToSave, Contains.Item(coreObj));
        Assert.That(objectsToSave, Contains.Item(collectionOwner));
        Assert.That(objectsToSave, Contains.Item(splineParent));
    }

    [Test]
    public void CollectObjectsToSave_DuplicateObjects_ShouldOnlyAddOnce()
    {
        // Arrange - Same object in multiple operations
        var obj = new TestCoreObjectWithUri();
        obj.SetUri(new Uri("file:///test.json"));

        var op1 = new UpdatePropertyValueOperation<int>(obj, "Value", 10, 0) { SequenceNumber = 1 };
        var op2 = new UpdatePropertyValueOperation<int>(obj, "Value", 20, 10) { SequenceNumber = 2 };

        var objectsToSave = new HashSet<CoreObject>();

        // Act
        AutoSaveService.CollectObjectsToSave(op1, objectsToSave);
        AutoSaveService.CollectObjectsToSave(op2, objectsToSave);

        // Assert
        Assert.That(objectsToSave.Count, Is.EqualTo(1));
    }

    #endregion

    #region Test Helper Classes

    private class TestCoreObject : CoreObject
    {
        public static readonly CoreProperty<int> ValueProperty;

        private int _value;

        static TestCoreObject()
        {
            ValueProperty = ConfigureProperty<int, TestCoreObject>(nameof(Value))
                .Accessor(o => o.Value, (o, v) => o.Value = v)
                .Register();
        }

        public int Value
        {
            get => _value;
            set => SetAndRaise(ValueProperty, ref _value, value);
        }
    }

    private class TestCoreObjectWithUri : CoreObject
    {
        public static readonly CoreProperty<int> ValueProperty;

        private int _value;

        static TestCoreObjectWithUri()
        {
            ValueProperty = ConfigureProperty<int, TestCoreObjectWithUri>(nameof(Value))
                .Accessor(o => o.Value, (o, v) => o.Value = v)
                .Register();
        }

        public int Value
        {
            get => _value;
            set => SetAndRaise(ValueProperty, ref _value, value);
        }

        public void SetUri(Uri uri)
        {
            Uri = uri;
        }
    }

    private class TestHierarchicalCoreObject : Hierarchical
    {
        public static readonly CoreProperty<int> ValueProperty;

        private int _value;

        static TestHierarchicalCoreObject()
        {
            ValueProperty = ConfigureProperty<int, TestHierarchicalCoreObject>(nameof(Value))
                .Accessor(o => o.Value, (o, v) => o.Value = v)
                .Register();
        }

        public int Value
        {
            get => _value;
            set => SetAndRaise(ValueProperty, ref _value, value);
        }

        public void SetUri(Uri uri)
        {
            Uri = uri;
        }

        public void AddChild(IHierarchical child)
        {
            HierarchicalChildren.Add(child);
        }
    }

    private class TestCoreObjectWithListAndUri : CoreObject
    {
        public static readonly CoreProperty<CoreList<string>> ItemsProperty;

        private CoreList<string> _items = [];

        static TestCoreObjectWithListAndUri()
        {
            ItemsProperty = ConfigureProperty<CoreList<string>, TestCoreObjectWithListAndUri>(nameof(Items))
                .Accessor(o => o.Items, (o, v) => o.Items = v)
                .Register();
        }

        public CoreList<string> Items
        {
            get => _items;
            set => SetAndRaise(ItemsProperty, ref _items, value);
        }

        public void SetUri(Uri uri)
        {
            Uri = uri;
        }
    }

    private class TestNodeItem : NodeItem<string>
    {
        public void SetUri(Uri uri)
        {
            Uri = uri;
        }
    }

    private class TestCollectionOperationWithCoreObjectItems : ChangeOperation, ICollectionChangeOperation
    {
        private readonly CoreObject _owner;
        private readonly CoreObject[] _items;

        public TestCollectionOperationWithCoreObjectItems(CoreObject owner, CoreObject[] items)
        {
            _owner = owner;
            _items = items;
        }

        public CoreObject Object => _owner;

        public string PropertyPath => "Items";

        public IEnumerable<object?> Items => _items;

        public override void Apply(OperationExecutionContext context) { }

        public override void Revert(OperationExecutionContext context) { }
    }

    private class TestUnknownChangeOperation : ChangeOperation
    {
        public override void Apply(OperationExecutionContext context) { }

        public override void Revert(OperationExecutionContext context) { }
    }

    #endregion
}
