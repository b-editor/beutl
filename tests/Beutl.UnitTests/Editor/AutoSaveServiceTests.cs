using System.Reactive.Linq;
using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor;
using Beutl.Editor.Operations;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.NodeGraph;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor;

public class AutoSaveServiceTests
{
    private string _directory = null!;

    [SetUp]
    public void Setup()
    {
        _directory = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.WorkDirectory,
            "beutl-autosave-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private Uri CreateUri(string relativePath) => new(Path.Combine(_directory, relativePath));

    [TestCase(false)]
    [TestCase(true)]
    public void AutoSave_RestoredElementSurvivesSceneReload(bool undoAddition)
    {
        var scene = new Scene { Uri = CreateUri("main.scene") };
        var element = new Element
        {
            Uri = CreateUri("elements/restored.belm"),
            Length = TimeSpan.FromSeconds(1),
        };
        CoreSerializer.StoreToUri(scene, scene.Uri);
        CoreSerializer.StoreToUri(element, element.Uri);
        using var harness = new HistoryHarness(scene);
        using var service = new AutoSaveService();
        var errors = new List<Exception>();
        using var errorSubscription = service.SaveError.Subscribe(errors.Add);
        using var saveSubscription = harness.Observer.Operations
            .Buffer(harness.History.StateChanged)
            .Subscribe(operations => service.AutoSave(operations));

        scene.AddChild(element);
        harness.History.Commit("Add element");
        Assert.That(File.Exists(element.Uri.LocalPath), Is.True);

        if (undoAddition)
            harness.History.Undo();
        else
            new ElementStructureService(harness.History).Exclude(scene, [element]);

        Assert.Multiple(() =>
        {
            Assert.That(scene.Children, Is.Empty);
            Assert.That(File.Exists(element.Uri.LocalPath), Is.False);
            Assert.That(errors, Is.Empty);
        });

        if (undoAddition)
            harness.History.Redo();
        else
            harness.History.Undo();

        Assert.That(scene.Children, Is.EqualTo(new[] { element }));
        for (int i = 0; i < 3; i++)
        {
            element.Name = $"Restored element {i}";
            harness.History.Commit("Rename element");
        }

        Scene restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);
        JsonNode patterns = JsonNode.Parse(File.ReadAllText(scene.Uri.LocalPath))!["Elements"]!;
        JsonNode include = patterns["Include"]!;
        string[] includes = include is JsonArray array
            ? array.Select(node => node!.GetValue<string>()).ToArray()
            : [include.GetValue<string>()];
        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty);
            Assert.That(File.Exists(element.Uri.LocalPath), Is.True);
            Assert.That(restored.Children.Select(child => child.Id), Is.EqualTo(new[] { element.Id }));
            Assert.That(restored.Children.Select(child => child.Name), Is.EqualTo(new[] { element.Name }));
            Assert.That(patterns["Exclude"], Is.Null);
            Assert.That(includes, Is.Unique);
        });
    }

    [Test]
    public void AutoSave_StandaloneSceneKeepsSceneAndAttachedElements()
    {
        var scene = new Beutl.ProjectSystem.Scene { Uri = CreateUri("main.scene") };
        var element = new Beutl.ProjectSystem.Element { Uri = CreateUri("element.belm") };
        scene.Children.Add(element);
        using var service = new AutoSaveService();
        service.SaveObjects([scene, element]);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(scene.Uri.LocalPath), Is.True);
            Assert.That(File.Exists(element.Uri.LocalPath), Is.True);
        });
        scene.Children.Remove(element);
        service.SaveObjects([element]);
        Assert.That(File.Exists(element.Uri.LocalPath), Is.False);
    }

    [Test]
    public void RemovedFileBackedEngineObject_DeletesItsSidecarAndRestoresItOnUndo()
    {
        var scene = new Beutl.ProjectSystem.Scene { Uri = CreateUri("main.scene") };
        var element = new Beutl.ProjectSystem.Element { Uri = CreateUri("element.belm") };
        var shape = new Beutl.Graphics.Shapes.RectShape { Uri = CreateUri("shape.json") };
        scene.Children.Add(element);
        element.Objects.Add(shape);
        using var service = new AutoSaveService();
        var errors = new List<Exception>();
        using var subscription = service.SaveError.Subscribe(errors.Add);
        service.SaveObjects([scene, element, shape]);
        Assert.That(File.Exists(shape.Uri.LocalPath), Is.True);
        element.Objects.Remove(shape);
        var removal = new RemoveCollectionRangeOperation<EngineObject>
        {
            Object = element,
            PropertyPath = nameof(element.Objects),
            Items = [shape],
            Index = 0,
            SequenceNumber = 1,
        };

        service.AutoSave([removal]);
        Assert.That(File.Exists(shape.Uri.LocalPath), Is.False);
        Assert.That(File.Exists(element.Uri.LocalPath), Is.True);
        Assert.That(File.Exists(scene.Uri.LocalPath), Is.True);

        element.Objects.Add(shape);
        service.AutoSave([removal]);
        Assert.That(File.Exists(shape.Uri.LocalPath), Is.True);
        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void StandaloneFileBackedEngineObject_IsSavedWithoutACollectionRemoval()
    {
        var shape = new Beutl.Graphics.Shapes.RectShape { Uri = CreateUri("shape.json") };
        using var service = new AutoSaveService();
        service.SaveObjects([shape]);
        Assert.That(File.Exists(shape.Uri.LocalPath), Is.True);
    }

    #region Constructor and Dispose Tests

    [Test]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        var service = new AutoSaveService();

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
        var service = new AutoSaveService();
        service.Dispose();

        Assert.Throws<ObjectDisposedException>(() => service.AutoSave([]));
    }

    [Test]
    public void SaveObjects_AfterDispose_ShouldThrowObjectDisposedException()
    {
        var service = new AutoSaveService();
        service.Dispose();

        Assert.Throws<ObjectDisposedException>(() => service.SaveObjects([]));
    }

    #endregion

    #region CollectObjectsToSave Tests - IUpdatePropertyValueOperation

    [Test]
    public void CollectObjectsToSave_WithUpdatePropertyValueOperation_ShouldAddObjectWithUri()
    {
        var obj = new TestCoreObjectWithUri();
        obj.SetUri(CreateUri("test.json"));

        var operation = new UpdatePropertyValueOperation<int>(obj, "Value", 10, 0)
        {
            SequenceNumber = 1
        };

        var objectsToSave = new HashSet<CoreObject>();

        AutoSaveService.CollectObjectsToSave(operation, objectsToSave);

        Assert.That(objectsToSave, Contains.Item(obj));
    }

    [Test]
    public void CollectObjectsToSave_WithUpdatePropertyValueOperation_ObjectWithoutUri_ShouldNotAddObject()
    {
        var obj = new TestCoreObject();

        var operation = new UpdatePropertyValueOperation<int>(obj, "Value", 10, 0)
        {
            SequenceNumber = 1
        };

        var objectsToSave = new HashSet<CoreObject>();

        AutoSaveService.CollectObjectsToSave(operation, objectsToSave);

        Assert.That(objectsToSave, Does.Not.Contain(obj));
    }

    [Test]
    public void CollectObjectsToSave_WithUpdatePropertyValueOperation_ShouldAddAncestorsWithUri()
    {
        var parent = new TestHierarchicalCoreObject();
        parent.SetUri(CreateUri("parent.json"));

        var child = new TestHierarchicalCoreObject();
        parent.AddChild(child);

        var operation = new UpdatePropertyValueOperation<int>(child, "Value", 10, 0)
        {
            SequenceNumber = 1
        };

        var objectsToSave = new HashSet<CoreObject>();

        AutoSaveService.CollectObjectsToSave(operation, objectsToSave);

        Assert.That(objectsToSave, Contains.Item(parent));
    }

    #endregion

    #region CollectObjectsToSave Tests - ICollectionChangeOperation

    [Test]
    public void CollectObjectsToSave_WithCollectionOperation_ShouldAddObjectWithUri()
    {
        var owner = new TestCoreObjectWithUri();
        owner.SetUri(CreateUri("owner.json"));

        var operation = new InsertCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Items = ["NewItem"],
            Index = 0,
            SequenceNumber = 1
        };

        var objectsToSave = new HashSet<CoreObject>();

        AutoSaveService.CollectObjectsToSave(operation, objectsToSave);

        Assert.That(objectsToSave, Contains.Item(owner));
    }

    [Test]
    public void CollectObjectsToSave_WithCollectionOperation_ShouldAddCoreObjectItems()
    {
        var owner = new TestCoreObjectWithUri();
        owner.SetUri(CreateUri("owner.json"));

        var item = new TestCoreObjectWithUri();
        item.SetUri(CreateUri("item.json"));

        var operation = new TestCollectionOperationWithCoreObjectItems(owner, [item])
        {
            SequenceNumber = 1
        };

        var objectsToSave = new HashSet<CoreObject>();

        AutoSaveService.CollectObjectsToSave(operation, objectsToSave);

        Assert.That(objectsToSave, Contains.Item(owner));
        Assert.That(objectsToSave, Contains.Item(item));
    }

    [Test]
    public void CollectObjectsToSave_WithCollectionOperation_ShouldAddAncestorsOfItems()
    {
        var owner = new TestCoreObjectWithUri();

        var itemParent = new TestHierarchicalCoreObject();
        itemParent.SetUri(CreateUri("item-parent.json"));

        var item = new TestHierarchicalCoreObject();
        itemParent.AddChild(item);

        var operation = new TestCollectionOperationWithCoreObjectItems(owner, [item])
        {
            SequenceNumber = 1
        };

        var objectsToSave = new HashSet<CoreObject>();

        AutoSaveService.CollectObjectsToSave(operation, objectsToSave);

        Assert.That(objectsToSave, Contains.Item(itemParent));
    }

    #endregion

    #region CollectObjectsToSave Tests - UpdateSplineEasingOperation

    [Test]
    public void CollectObjectsToSave_WithSplineEasingOperation_ShouldAddParentWithUri()
    {
        var parent = new TestCoreObjectWithUri();
        parent.SetUri(CreateUri("parent.json"));

        var easing = new SplineEasing();
        var operation = new UpdateSplineEasingOperation(easing, "X1", 0.5f, 0f)
        {
            Parent = parent,
            SequenceNumber = 1
        };

        var objectsToSave = new HashSet<CoreObject>();

        AutoSaveService.CollectObjectsToSave(operation, objectsToSave);

        Assert.That(objectsToSave, Contains.Item(parent));
    }

    [Test]
    public void CollectObjectsToSave_WithSplineEasingOperation_NullParent_ShouldNotThrow()
    {
        var easing = new SplineEasing();
        var operation = new UpdateSplineEasingOperation(easing, "X1", 0.5f, 0f)
        {
            Parent = null,
            SequenceNumber = 1
        };

        var objectsToSave = new HashSet<CoreObject>();

        Assert.DoesNotThrow(() => AutoSaveService.CollectObjectsToSave(operation, objectsToSave));
        Assert.That(objectsToSave, Is.Empty);
    }

    #endregion

    #region CollectObjectsToSave Tests - UpdateNodeMemberOperation

    [Test]
    public void CollectObjectsToSave_WithNodeMemberOperation_CoreObjectNodeMember_ShouldAddNodeMember()
    {
        var nodeMember = new TestNodeMember();
        nodeMember.SetUri(CreateUri("nodeMember.json"));

        var operation = new UpdateNodeMemberOperation(nodeMember, "Property", "new", "old")
        {
            SequenceNumber = 1
        };

        var objectsToSave = new HashSet<CoreObject>();

        AutoSaveService.CollectObjectsToSave(operation, objectsToSave);

        Assert.That(objectsToSave, Contains.Item(nodeMember));
    }

    #endregion

    #region CollectObjectsToSave Tests - Unknown Operation Type

    [Test]
    public void CollectObjectsToSave_WithUnknownOperationType_ShouldNotThrow()
    {
        var operation = new TestUnknownChangeOperation
        {
            SequenceNumber = 1
        };

        var objectsToSave = new HashSet<CoreObject>();

        Assert.DoesNotThrow(() => AutoSaveService.CollectObjectsToSave(operation, objectsToSave));
        Assert.That(objectsToSave, Is.Empty);
    }

    #endregion

    #region AddObjectWithAncestors Tests

    [Test]
    public void AddObjectWithAncestors_ObjectWithUri_ShouldAddObject()
    {
        var obj = new TestCoreObjectWithUri();
        obj.SetUri(CreateUri("test.json"));

        var objectsToSave = new HashSet<CoreObject>();

        AutoSaveService.AddObjectWithAncestors(obj, objectsToSave);

        Assert.That(objectsToSave, Contains.Item(obj));
    }

    [Test]
    public void AddObjectWithAncestors_ObjectWithoutUri_ShouldNotAddObject()
    {
        var obj = new TestCoreObject();

        var objectsToSave = new HashSet<CoreObject>();

        AutoSaveService.AddObjectWithAncestors(obj, objectsToSave);

        Assert.That(objectsToSave, Does.Not.Contain(obj));
    }

    [Test]
    public void AddObjectWithAncestors_WithHierarchy_ShouldAddAllAncestorsWithUri()
    {
        var root = new TestHierarchicalCoreObject();
        root.SetUri(CreateUri("root.json"));

        var parent = new TestHierarchicalCoreObject();
        parent.SetUri(CreateUri("parent.json"));
        root.AddChild(parent);

        var child = new TestHierarchicalCoreObject();
        parent.AddChild(child);

        var objectsToSave = new HashSet<CoreObject>();

        AutoSaveService.AddObjectWithAncestors(child, objectsToSave);

        Assert.That(objectsToSave, Contains.Item(root));
        Assert.That(objectsToSave, Contains.Item(parent));
        Assert.That(objectsToSave, Does.Not.Contain(child));
    }

    #endregion

    #region AutoSave Tests

    [Test]
    public void AutoSave_WithEmptyOperations_ShouldNotThrow()
    {
        using var service = new AutoSaveService();

        Assert.DoesNotThrow(() => service.AutoSave([]));
    }

    [Test]
    public void AutoSave_WithMultipleOperations_ShouldCollectAllObjects()
    {
        using var service = new AutoSaveService();

        var obj1 = new TestCoreObjectWithUri();
        obj1.SetUri(CreateUri("nonexistent/test1.json"));

        var obj2 = new TestCoreObjectWithUri();
        obj2.SetUri(CreateUri("nonexistent/test2.json"));

        var op1 = new UpdatePropertyValueOperation<int>(obj1, "Value", 10, 0)
        {
            SequenceNumber = 1
        };
        var op2 = new UpdatePropertyValueOperation<int>(obj2, "Value", 20, 0)
        {
            SequenceNumber = 2
        };

        var errors = new List<Exception>();
        using var subscription = service.SaveError.Subscribe(errors.Add);

        service.AutoSave([op1, op2]);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(obj1.Uri!.LocalPath), Is.True);
            Assert.That(File.Exists(obj2.Uri!.LocalPath), Is.True);
            Assert.That(errors, Is.Empty);
        });
    }

    #endregion

    #region SaveObjects Tests

    [Test]
    public void SaveObjects_WithEmptyCollection_ShouldNotThrow()
    {
        using var service = new AutoSaveService();

        Assert.DoesNotThrow(() => service.SaveObjects([]));
    }

    [Test]
    public void SaveObjects_WithNonExistentDirectory_ShouldCreateDirectoryAndSaveObject()
    {
        using var service = new AutoSaveService();
        var obj = new TestCoreObjectWithUri();
        obj.SetUri(CreateUri("nonexistent/path/that/does/not/exist/test.json"));

        Exception? receivedError = null;
        using var subscription = service.SaveError.Subscribe(ex => receivedError = ex);

        service.SaveObjects([obj]);

        Assert.Multiple(() =>
        {
            Assert.That(receivedError, Is.Null);
            Assert.That(File.Exists(obj.Uri!.LocalPath), Is.True);
        });
    }

    [Test]
    public void SaveObjects_WhenExceptionOccurs_ShouldContinueWithNextObject()
    {
        using var service = new AutoSaveService();

        var obj1 = new TestCoreObjectWithUri();
        obj1.SetUri(CreateUri("nonexistent1/test.json"));
        var failure = new InvalidOperationException("Injected save failure.");
        obj1.SaveFailure = failure;

        var obj2 = new TestCoreObjectWithUri();
        obj2.SetUri(CreateUri("nonexistent2/test.json"));

        var errors = new List<Exception>();
        using var subscription = service.SaveError.Subscribe(errors.Add);

        // Should not throw, even if saving fails
        Assert.DoesNotThrow(() => service.SaveObjects([obj1, obj2]));

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.EqualTo(new[] { failure }));
            Assert.That(File.Exists(obj2.Uri!.LocalPath), Is.True,
                "The next object must still be saved after a preceding serialization failure.");
        });
    }

    #endregion

    #region SaveError Observable Tests

    [Test]
    public void SaveError_Subscription_ShouldReceiveErrors()
    {
        using var service = new AutoSaveService();
        var obj = new TestCoreObjectWithUri();
        obj.SetUri(CreateUri("this/path/does/not/exist/test.json"));
        var failure = new InvalidOperationException("Injected save failure.");
        obj.SaveFailure = failure;

        var errors = new List<Exception>();
        using var subscription = service.SaveError.Subscribe(ex => errors.Add(ex));

        service.SaveObjects([obj]);

        Assert.That(errors, Is.EqualTo(new[] { failure }));
    }

    [Test]
    public void SaveError_MultipleSubscribers_ShouldAllReceiveErrors()
    {
        using var service = new AutoSaveService();
        var obj = new TestCoreObjectWithUri();
        obj.SetUri(CreateUri("nonexistent/test.json"));
        var failure = new InvalidOperationException("Injected save failure.");
        obj.SaveFailure = failure;

        var errors1 = new List<Exception>();
        var errors2 = new List<Exception>();
        using var sub1 = service.SaveError.Subscribe(ex => errors1.Add(ex));
        using var sub2 = service.SaveError.Subscribe(ex => errors2.Add(ex));

        service.SaveObjects([obj]);

        Assert.Multiple(() =>
        {
            Assert.That(errors1, Is.EqualTo(new[] { failure }));
            Assert.That(errors2, Is.EqualTo(new[] { failure }));
        });
    }

    #endregion

    #region Test Helper Classes

    private class TestCoreObject : CoreObject;

    private class TestCoreObjectWithUri : CoreObject
    {
        public Exception? SaveFailure { get; set; }

        public void SetUri(Uri uri)
        {
            Uri = uri;
        }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            if (SaveFailure is not null) throw SaveFailure;
        }
    }

    private class TestHierarchicalCoreObject : Hierarchical
    {
        public void SetUri(Uri uri)
        {
            Uri = uri;
        }

        public void AddChild(IHierarchical child)
        {
            HierarchicalChildren.Add(child);
        }
    }

    [SuppressResourceClassGeneration]
    private partial class TestNodeMember : NodeMember<string>
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

        public override void Apply() { }

        public override void Revert() { }
    }

    private class TestUnknownChangeOperation : ChangeOperation
    {
        public override void Apply() { }

        public override void Revert() { }
    }

    #endregion
}
