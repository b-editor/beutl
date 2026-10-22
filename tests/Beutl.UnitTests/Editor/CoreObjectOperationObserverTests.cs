using System.Reactive;
using System.Reactive.Linq;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Collections;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Operations;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.NodeGraph;

namespace Beutl.UnitTests.Editor;

public class CoreObjectOperationObserverTests
{
    private OperationSequenceGenerator _sequenceGenerator = null!;

    [SetUp]
    public void Setup()
    {
        _sequenceGenerator = new OperationSequenceGenerator();
    }

    #region Constructor Tests

    [Test]
    public void Constructor_WithPropertyPath_ShouldBuildCorrectPath()
    {
        var obj = new TestCoreObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, obj, _sequenceGenerator, "Parent");
        obj.StringValue = "changed";

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<string>)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("Parent.StringValue"));
    }

    [Test]
    public void Constructor_WithPropertyPathsToTrack_ShouldOnlyTrackSpecifiedProperties()
    {
        var obj = new TestCoreObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        var propertyPathsToTrack = new HashSet<string> { "StringValue" };

        using var operationObserver = new CoreObjectOperationObserver(
            testObserver, obj, _sequenceGenerator, "", propertyPathsToTrack);

        obj.StringValue = "changed";
        obj.IntValue = 100;

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<string>)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("StringValue"));
    }

    [Test]
    public void Constructor_WithCollection_ShouldInitializeCollectionPublisher()
    {
        var parentObj = new TestCollectionCoreObject();
        var childItem = new TestCoreObject { StringValue = "item1" };
        parentObj.Items.Add(childItem);

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, parentObj, _sequenceGenerator);
        childItem.StringValue = "modified";

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    #endregion

    #region Dispose Tests

    [Test]
    public void Dispose_ShouldUnsubscribeFromPropertyChanged()
    {
        var obj = new TestCoreObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var operationObserver = new CoreObjectOperationObserver(testObserver, obj, _sequenceGenerator);

        operationObserver.Dispose();
        obj.StringValue = "changed";

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Dispose_ShouldCompleteOperationsObservable()
    {
        var obj = new TestCoreObject();
        bool completed = false;

        var operationObserver = new CoreObjectOperationObserver(null, obj, _sequenceGenerator);
        using var subscription = operationObserver.Operations.Subscribe(_ => { }, () => completed = true);

        operationObserver.Dispose();

        Assert.That(completed, Is.True);
    }

    [Test]
    public void Dispose_ShouldDisposeCollectionPublishers()
    {
        var parentObj = new TestCollectionCoreObject();
        var childItem = new TestCoreObject { StringValue = "item1" };
        parentObj.Items.Add(childItem);

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var operationObserver = new CoreObjectOperationObserver(testObserver, parentObj, _sequenceGenerator);

        operationObserver.Dispose();
        childItem.StringValue = "modified";

        Assert.That(receivedOperations, Is.Empty);
    }

    #endregion

    #region OnPropertyChanged Tests

    [Test]
    public void OnPropertyChanged_ShouldPublishUpdatePropertyValueOperation()
    {
        var obj = new TestCoreObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, obj, _sequenceGenerator);

        obj.StringValue = "new value";

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<string>)receivedOperations[0];
        Assert.Multiple(() =>
        {
            Assert.That(operation.PropertyPath, Is.EqualTo("StringValue"));
            Assert.That(operation.NewValue, Is.EqualTo("new value"));
            Assert.That(operation.OldValue, Is.Null);
            Assert.That(operation.SequenceNumber, Is.GreaterThan(0));
        });
    }

    [Test]
    public void OnPropertyChanged_AfterSuppressionEnds_ShouldPublish()
    {
        var obj = new TestCoreObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, obj, _sequenceGenerator);

        using (PublishingSuppression.Enter())
        {
            obj.StringValue = "suppressed";
        }
        obj.StringValue = "not suppressed";

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<string>)receivedOperations[0];
        Assert.That(operation.NewValue, Is.EqualTo("not suppressed"));
    }

    [Test]
    public void OnPropertyChanged_WithNotTrackedProperty_ShouldNotPublish()
    {
        var obj = new TestCoreObjectWithNotTrackedProperty();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, obj, _sequenceGenerator);

        obj.NotTrackedValue = "changed";

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void OnPropertyChanged_WithTrackedProperty_ShouldPublish()
    {
        var obj = new TestCoreObjectWithNotTrackedProperty();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, obj, _sequenceGenerator);

        obj.TrackedValue = "changed";

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    [Test]
    public void OnPropertyChanged_WithNewChildObject_ShouldCreateNewChildPublisher()
    {
        var parentObj = new TestParentCoreObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, parentObj, _sequenceGenerator);
        receivedOperations.Clear();

        var newChild = new TestCoreObject { StringValue = "new child" };
        parentObj.Child = newChild;

        // First operation is for setting Child property
        receivedOperations.Clear();

        // Modify new child
        newChild.StringValue = "modified";

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<string>)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("Child.StringValue"));
    }

    [Test]
    public void OnPropertyChanged_WithReplacedChildObject_ShouldDisposeOldPublisher()
    {
        var oldChild = new TestCoreObject { StringValue = "old" };
        var parentObj = new TestParentCoreObject { Child = oldChild };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, parentObj, _sequenceGenerator);

        var newChild = new TestCoreObject { StringValue = "new" };
        parentObj.Child = newChild;
        receivedOperations.Clear();

        oldChild.StringValue = "should not be tracked";

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void OnPropertyChanged_WithNewList_ShouldCreateNewCollectionPublisher()
    {
        var parentObj = new TestCollectionCoreObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, parentObj, _sequenceGenerator);
        receivedOperations.Clear();

        var newList = new CoreList<TestCoreObject>();
        var item = new TestCoreObject { StringValue = "new item" };
        newList.Add(item);
        parentObj.Items = newList;

        receivedOperations.Clear();

        var anotherItem = new TestCoreObject { StringValue = "another" };
        parentObj.Items.Add(anotherItem);

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    [Test]
    public void OnPropertyChanged_MultipleSequentialChanges_ShouldPublishAllOperations()
    {
        var obj = new TestCoreObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, obj, _sequenceGenerator);

        obj.StringValue = "first";
        obj.StringValue = "second";
        obj.StringValue = "third";

        Assert.That(receivedOperations, Has.Count.EqualTo(3));
        var operations = receivedOperations.Cast<UpdatePropertyValueOperation<string>>().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(operations[0].NewValue, Is.EqualTo("first"));
            Assert.That(operations[1].NewValue, Is.EqualTo("second"));
            Assert.That(operations[2].NewValue, Is.EqualTo("third"));
            Assert.That(operations[1].SequenceNumber, Is.GreaterThan(operations[0].SequenceNumber));
            Assert.That(operations[2].SequenceNumber, Is.GreaterThan(operations[1].SequenceNumber));
        });
    }

    #endregion

    #region KeyFrame SplineEasing Tests

    [Test]
    public void Constructor_WithKeyFrameAndSplineEasing_ShouldInitializeSplineEasingPublisher()
    {
        var keyFrame = new KeyFrame<float>
        {
            Value = 1.0f,
            KeyTime = TimeSpan.FromSeconds(1),
            Easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f)
        };

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, keyFrame, _sequenceGenerator);

        var splineEasing = (SplineEasing)keyFrame.Easing;
        splineEasing.X1 = 0.5f;

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    [Test]
    public void OnPropertyChanged_WhenEasingChangedToSplineEasing_ShouldCreateNewSplinePublisher()
    {
        var keyFrame = new KeyFrame<float>
        {
            Value = 1.0f,
            KeyTime = TimeSpan.FromSeconds(1),
            Easing = new LinearEasing()
        };

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, keyFrame, _sequenceGenerator);
        receivedOperations.Clear();

        var newSplineEasing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        keyFrame.Easing = newSplineEasing;

        receivedOperations.Clear();

        newSplineEasing.X1 = 0.5f;

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    [Test]
    public void OnPropertyChanged_WhenEasingChangedFromSplineEasing_ShouldDisposeOldSplinePublisher()
    {
        var oldSplineEasing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var keyFrame = new KeyFrame<float>
        {
            Value = 1.0f,
            KeyTime = TimeSpan.FromSeconds(1),
            Easing = oldSplineEasing
        };

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, keyFrame, _sequenceGenerator);

        keyFrame.Easing = new LinearEasing();
        receivedOperations.Clear();

        oldSplineEasing.X1 = 0.5f;

        Assert.That(receivedOperations, Is.Empty);
    }

    #endregion

    #region Deep Nesting Tests

    [Test]
    public void Constructor_WithDeepNestedObjects_ShouldTrackAllLevels()
    {
        var innerChild = new TestCoreObject { StringValue = "inner" };
        var middleChild = new TestParentCoreObject { Child = innerChild };
        var parentObj = new TestGrandParentCoreObject { MiddleChild = middleChild };

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, parentObj, _sequenceGenerator);
        innerChild.StringValue = "modified";

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<string>)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("MiddleChild.Child.StringValue"));
    }

    [Test]
    public void Dispose_WithDeepNestedObjects_ShouldStopAllTracking()
    {
        var innerChild = new TestCoreObject { StringValue = "inner" };
        var middleChild = new TestParentCoreObject { Child = innerChild };
        var parentObj = new TestGrandParentCoreObject { MiddleChild = middleChild };

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var operationObserver = new CoreObjectOperationObserver(testObserver, parentObj, _sequenceGenerator);

        operationObserver.Dispose();
        innerChild.StringValue = "should not track";

        Assert.That(receivedOperations, Is.Empty);
    }

    #endregion

    #region PropertyPathsToTrack Advanced Tests

    [Test]
    public void Constructor_WithNestedPropertyPathsToTrack_ShouldOnlyTrackSpecified()
    {
        var child = new TestCoreObject();
        var parent = new TestParentCoreObject { Child = child };

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        var propertyPathsToTrack = new HashSet<string> { "Child.StringValue" };

        using var operationObserver = new CoreObjectOperationObserver(
            testObserver, parent, _sequenceGenerator, "", propertyPathsToTrack);

        child.StringValue = "tracked";
        child.IntValue = 100;

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<string>)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("Child.StringValue"));
    }

    #endregion

    #region Hierarchical Tests

    [Test]
    public void OnPropertyChanged_WithHierarchicalObject_ShouldNotTrackHierarchicalParent()
    {
        var parent = new TestHierarchicalObject();
        var child = new TestHierarchicalObject();

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, child, _sequenceGenerator);

        // Simulate hierarchy attachment (internally sets HierarchicalParent)
        parent.AddTestChild(child);

        // HierarchicalParent changes should not be published
        var hierarchicalParentOps = receivedOperations
            .Where(op => op is UpdatePropertyValueOperation<IHierarchical> update && update.PropertyPath == "HierarchicalParent")
            .ToList();
        Assert.That(hierarchicalParentOps, Is.Empty);
    }

    [Test]
    public void OnPropertyChanged_WithHierarchicalObject_ShouldTrackOtherProperties()
    {
        var obj = new TestHierarchicalObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, obj, _sequenceGenerator);

        obj.Title = "changed";

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<string>)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("Title"));
    }

    #endregion

    #region EngineObject Tests

    [Test]
    public void Dispose_WithEngineObject_ShouldDisposeEnginePropertyPublishers()
    {
        var engineObj = new TestEngineObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var operationObserver = new CoreObjectOperationObserver(testObserver, engineObj, _sequenceGenerator);

        operationObserver.Dispose();
        engineObj.FloatValue.Value = 50f;

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void OnPropertyChanged_WithEngineObject_ShouldPublishEnginePropertyChanges()
    {
        var engineObj = new TestEngineObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, engineObj, _sequenceGenerator);

        engineObj.FloatValue.Value = 25f;
        engineObj.FloatValue.Value = 75f;

        Assert.That(receivedOperations, Has.Count.EqualTo(2));
    }

    [Test]
    public void OnPropertyChanged_WithEngineObjectAndPublishingSuppressed_ShouldNotPublish()
    {
        var engineObj = new TestEngineObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, engineObj, _sequenceGenerator);

        using (PublishingSuppression.Enter())
        {
            engineObj.FloatValue.Value = 50f;
        }

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Constructor_WithEngineObjectAndPropertyPathsToTrack_ShouldOnlyTrackSpecified()
    {
        var engineObj = new TestEngineObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        // Note: For EngineObject properties, the path must include sub-properties like "FloatValue.CurrentValue"
        // because the filter logic checks for sub-path containment
        var propertyPathsToTrack = new HashSet<string> { "FloatValue.CurrentValue" };

        using var operationObserver = new CoreObjectOperationObserver(
            testObserver, engineObj, _sequenceGenerator, "", propertyPathsToTrack);

        engineObj.FloatValue.Value = 50f;

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    #endregion

    #region NodeMember Tests

    [Test]
    public void Dispose_WithNodeMember_ShouldDisposeNodeMemberPublisher()
    {
        var nodeMember = new DefaultNodeMember<float>();
        var adapter = new NodePropertyAdapter<float>("TestProperty", 0f, null);
        nodeMember.SetProperty(adapter);

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var operationObserver = new CoreObjectOperationObserver(testObserver, nodeMember, _sequenceGenerator);

        operationObserver.Dispose();
        adapter.SetValue(50f);

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void OnPropertyChanged_WithNodeMember_ShouldPublishPropertyValueChanges()
    {
        var nodeMember = new DefaultNodeMember<float>();
        var adapter = new NodePropertyAdapter<float>("TestProperty", 0f, null);
        nodeMember.SetProperty(adapter);

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, nodeMember, _sequenceGenerator);

        adapter.SetValue(25f);
        adapter.SetValue(75f);

        Assert.That(receivedOperations, Has.Count.EqualTo(2));
    }

    [Test]
    public void OnPropertyChanged_WithNodeMemberAfterSuppressionEnds_ShouldPublish()
    {
        var nodeMember = new DefaultNodeMember<float>();
        var adapter = new NodePropertyAdapter<float>("TestProperty", 0f, null);
        nodeMember.SetProperty(adapter);

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, nodeMember, _sequenceGenerator);

        using (PublishingSuppression.Enter())
        {
            adapter.SetValue(25f);
        }
        adapter.SetValue(75f);

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    [Test]
    public void Constructor_WithNodeMemberAndAnimation_ShouldTrackAnimationChanges()
    {
        var animation = new KeyFrameAnimation<float>();
        var nodeMember = new DefaultNodeMember<float>();
        var adapter = new NodePropertyAdapter<float>("TestProperty", 0f, animation);
        nodeMember.SetProperty(adapter);

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, nodeMember, _sequenceGenerator);

        var newAnimation = new KeyFrameAnimation<float>();
        adapter.Animation = newAnimation;

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    [Test]
    public void OnPropertyChanged_WithNodeMemberAnimationChange_ShouldPublishAnimationChange()
    {
        var nodeMember = new DefaultNodeMember<float>();
        var adapter = new NodePropertyAdapter<float>("TestProperty", 0f, null);
        nodeMember.SetProperty(adapter);

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, nodeMember, _sequenceGenerator);

        adapter.Animation = new KeyFrameAnimation<float>();

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    [Test]
    public void Constructor_WithNodeMemberAndPropertyPathsToTrack_ShouldOnlyTrackSpecified()
    {
        var nodeMember = new DefaultNodeMember<float>();
        var adapter = new NodePropertyAdapter<float>("TestProperty", 0f, null);
        nodeMember.SetProperty(adapter);

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        var propertyPathsToTrack = new HashSet<string> { "Property" };

        using var operationObserver = new CoreObjectOperationObserver(
            testObserver, nodeMember, _sequenceGenerator, "", propertyPathsToTrack);

        adapter.SetValue(50f);

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    [Test]
    public void Constructor_WithNodeMemberAndPropertyPathsToTrackExcludingProperty_ShouldNotTrack()
    {
        var nodeMember = new DefaultNodeMember<float>();
        var adapter = new NodePropertyAdapter<float>("TestProperty", 0f, null);
        nodeMember.SetProperty(adapter);

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        var propertyPathsToTrack = new HashSet<string> { "Animation" };

        using var operationObserver = new CoreObjectOperationObserver(
            testObserver, nodeMember, _sequenceGenerator, "", propertyPathsToTrack);

        adapter.SetValue(50f);

        var propertyOps = receivedOperations.Where(op =>
            op is UpdateNodeMemberOperation update && update.PropertyPath == "Property").ToList();
        Assert.That(propertyOps, Is.Empty);
    }

    #endregion

    #region Test Helper Classes

    private class TestCoreObject : CoreObject
    {
        public static readonly CoreProperty<string?> StringValueProperty;
        public static readonly CoreProperty<int> IntValueProperty;

        private string? _stringValue;
        private int _intValue;

        static TestCoreObject()
        {
            StringValueProperty = ConfigureProperty<string?, TestCoreObject>(nameof(StringValue))
                .Accessor(o => o.StringValue, (o, v) => o.StringValue = v)
                .Register();

            IntValueProperty = ConfigureProperty<int, TestCoreObject>(nameof(IntValue))
                .Accessor(o => o.IntValue, (o, v) => o.IntValue = v)
                .Register();
        }

        public string? StringValue
        {
            get => _stringValue;
            set => SetAndRaise(StringValueProperty, ref _stringValue, value);
        }

        public int IntValue
        {
            get => _intValue;
            set => SetAndRaise(IntValueProperty, ref _intValue, value);
        }
    }

    private class TestParentCoreObject : CoreObject
    {
        public static readonly CoreProperty<TestCoreObject?> ChildProperty;

        private TestCoreObject? _child;

        static TestParentCoreObject()
        {
            ChildProperty = ConfigureProperty<TestCoreObject?, TestParentCoreObject>(nameof(Child))
                .Accessor(o => o.Child, (o, v) => o.Child = v)
                .Register();
        }

        public TestCoreObject? Child
        {
            get => _child;
            set => SetAndRaise(ChildProperty, ref _child, value);
        }
    }

    private class TestCollectionCoreObject : CoreObject
    {
        public static readonly CoreProperty<CoreList<TestCoreObject>> ItemsProperty;

        private CoreList<TestCoreObject> _items = new();

        static TestCollectionCoreObject()
        {
            ItemsProperty = ConfigureProperty<CoreList<TestCoreObject>, TestCollectionCoreObject>(nameof(Items))
                .Accessor(o => o.Items, (o, v) => o.Items = v)
                .Register();
        }

        public CoreList<TestCoreObject> Items
        {
            get => _items;
            set => SetAndRaise(ItemsProperty, ref _items, value);
        }
    }

    private class TestCoreObjectWithNotTrackedProperty : CoreObject
    {
        public static readonly CoreProperty<string?> NotTrackedValueProperty;
        public static readonly CoreProperty<string?> TrackedValueProperty;

        private string? _notTrackedValue;
        private string? _trackedValue;

        static TestCoreObjectWithNotTrackedProperty()
        {
            NotTrackedValueProperty = ConfigureProperty<string?, TestCoreObjectWithNotTrackedProperty>(nameof(NotTrackedValue))
                .Accessor(o => o.NotTrackedValue, (o, v) => o.NotTrackedValue = v)
                .Register();

            TrackedValueProperty = ConfigureProperty<string?, TestCoreObjectWithNotTrackedProperty>(nameof(TrackedValue))
                .Accessor(o => o.TrackedValue, (o, v) => o.TrackedValue = v)
                .Register();
        }

        [NotTracked]
        public string? NotTrackedValue
        {
            get => _notTrackedValue;
            set => SetAndRaise(NotTrackedValueProperty, ref _notTrackedValue, value);
        }

        public string? TrackedValue
        {
            get => _trackedValue;
            set => SetAndRaise(TrackedValueProperty, ref _trackedValue, value);
        }
    }

    private class TestGrandParentCoreObject : CoreObject
    {
        public static readonly CoreProperty<TestParentCoreObject?> MiddleChildProperty;

        private TestParentCoreObject? _middleChild;

        static TestGrandParentCoreObject()
        {
            MiddleChildProperty = ConfigureProperty<TestParentCoreObject?, TestGrandParentCoreObject>(nameof(MiddleChild))
                .Accessor(o => o.MiddleChild, (o, v) => o.MiddleChild = v)
                .Register();
        }

        public TestParentCoreObject? MiddleChild
        {
            get => _middleChild;
            set => SetAndRaise(MiddleChildProperty, ref _middleChild, value);
        }
    }

    private class TestHierarchicalObject : Hierarchical
    {
        public static readonly CoreProperty<string?> TitleProperty;

        private string? _title;

        static TestHierarchicalObject()
        {
            TitleProperty = ConfigureProperty<string?, TestHierarchicalObject>(nameof(Title))
                .Accessor(o => o.Title, (o, v) => o.Title = v)
                .Register();
        }

        public string? Title
        {
            get => _title;
            set => SetAndRaise(TitleProperty, ref _title, value);
        }

        public void AddTestChild(TestHierarchicalObject child)
        {
            HierarchicalChildren.Add(child);
        }
    }

    [SuppressResourceClassGeneration]
    private class TestEngineObject : EngineObject
    {
        public TestEngineObject()
        {
            ScanProperties<TestEngineObject>();
        }

        [System.ComponentModel.DataAnnotations.Range(0f, 100f)]
        public IExpressionProperty<float> FloatValue { get; } = Property.CreateAnimatable(0f);
    }

    #endregion
}
