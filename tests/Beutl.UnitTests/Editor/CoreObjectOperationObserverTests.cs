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
using Beutl.Logging;
using Beutl.NodeTree;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Editor;

public class CoreObjectOperationObserverTests
{
    private OperationSequenceGenerator _sequenceGenerator = null!;

    [SetUp]
    public void Setup()
    {
        Log.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
        _sequenceGenerator = new OperationSequenceGenerator();
    }

    [TearDown]
    public void TearDown()
    {
    }

    #region Constructor Tests

    [Test]
    public void Constructor_ShouldInitializeOperationsProperty()
    {
        // Arrange
        var obj = new TestCoreObject();

        // Act
        using var observer = new CoreObjectOperationObserver(null, obj, _sequenceGenerator);

        // Assert
        Assert.That(observer.Operations, Is.Not.Null);
    }

    [Test]
    public void Constructor_WithObserver_ShouldSubscribeToOperations()
    {
        // Arrange
        var obj = new TestCoreObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        // Act
        using var operationObserver = new CoreObjectOperationObserver(testObserver, obj, _sequenceGenerator);
        obj.StringValue = "changed";

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        Assert.That(receivedOperations[0], Is.TypeOf<UpdatePropertyValueOperation<string>>());
    }

    [Test]
    public void Constructor_WithNullObserver_ShouldNotThrow()
    {
        // Arrange
        var obj = new TestCoreObject();

        // Act & Assert
        Assert.DoesNotThrow(() =>
        {
            using var observer = new CoreObjectOperationObserver(null, obj, _sequenceGenerator);
        });
    }

    [Test]
    public void Constructor_WithEmptyPropertyPath_ShouldWorkCorrectly()
    {
        // Arrange
        var obj = new TestCoreObject();

        // Act
        using var observer = new CoreObjectOperationObserver(null, obj, _sequenceGenerator, "");

        // Assert
        Assert.That(observer.Operations, Is.Not.Null);
    }

    [Test]
    public void Constructor_WithPropertyPath_ShouldBuildCorrectPath()
    {
        // Arrange
        var obj = new TestCoreObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        // Act
        using var operationObserver = new CoreObjectOperationObserver(testObserver, obj, _sequenceGenerator, "Parent");
        obj.StringValue = "changed";

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<string>)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("Parent.StringValue"));
    }

    [Test]
    public void Constructor_WithPropertyPathsToTrack_ShouldOnlyTrackSpecifiedProperties()
    {
        // Arrange
        var obj = new TestCoreObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        var propertyPathsToTrack = new HashSet<string> { "StringValue" };

        // Act
        using var operationObserver = new CoreObjectOperationObserver(
            testObserver, obj, _sequenceGenerator, "", propertyPathsToTrack);

        obj.StringValue = "changed";
        obj.IntValue = 100;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<string>)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("StringValue"));
    }

    [Test]
    public void Constructor_WithChildCoreObject_ShouldInitializeChildPublisher()
    {
        // Arrange
        var childObj = new TestCoreObject { StringValue = "child" };
        var parentObj = new TestParentCoreObject { Child = childObj };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        // Act
        using var operationObserver = new CoreObjectOperationObserver(testObserver, parentObj, _sequenceGenerator);
        childObj.StringValue = "modified";

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<string>)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("Child.StringValue"));
    }

    [Test]
    public void Constructor_WithCollection_ShouldInitializeCollectionPublisher()
    {
        // Arrange
        var parentObj = new TestCollectionCoreObject();
        var childItem = new TestCoreObject { StringValue = "item1" };
        parentObj.Items.Add(childItem);

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        // Act
        using var operationObserver = new CoreObjectOperationObserver(testObserver, parentObj, _sequenceGenerator);
        childItem.StringValue = "modified";

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    #endregion

    #region Dispose Tests

    [Test]
    public void Dispose_ShouldUnsubscribeFromPropertyChanged()
    {
        // Arrange
        var obj = new TestCoreObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var operationObserver = new CoreObjectOperationObserver(testObserver, obj, _sequenceGenerator);

        // Act
        operationObserver.Dispose();
        obj.StringValue = "changed";

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Dispose_ShouldCompleteOperationsObservable()
    {
        // Arrange
        var obj = new TestCoreObject();
        bool completed = false;

        var operationObserver = new CoreObjectOperationObserver(null, obj, _sequenceGenerator);
        // Subscribe to Operations after creation
        using var subscription = operationObserver.Operations.Subscribe(_ => { }, () => completed = true);

        // Act
        operationObserver.Dispose();

        // Assert
        Assert.That(completed, Is.True);
    }

    [Test]
    public void Dispose_ShouldDisposeChildPublishers()
    {
        // Arrange
        var childObj = new TestCoreObject { StringValue = "child" };
        var parentObj = new TestParentCoreObject { Child = childObj };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var operationObserver = new CoreObjectOperationObserver(testObserver, parentObj, _sequenceGenerator);

        // Act
        operationObserver.Dispose();
        childObj.StringValue = "modified";

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Dispose_ShouldDisposeCollectionPublishers()
    {
        // Arrange
        var parentObj = new TestCollectionCoreObject();
        var childItem = new TestCoreObject { StringValue = "item1" };
        parentObj.Items.Add(childItem);

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var operationObserver = new CoreObjectOperationObserver(testObserver, parentObj, _sequenceGenerator);

        // Act
        operationObserver.Dispose();
        childItem.StringValue = "modified";

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    #endregion

    #region OnPropertyChanged Tests

    [Test]
    public void OnPropertyChanged_ShouldPublishUpdatePropertyValueOperation()
    {
        // Arrange
        var obj = new TestCoreObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, obj, _sequenceGenerator);

        // Act
        obj.StringValue = "new value";

        // Assert
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
    public void OnPropertyChanged_WithPublishingSuppressed_ShouldNotPublish()
    {
        // Arrange
        var obj = new TestCoreObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, obj, _sequenceGenerator);

        // Act
        using (PublishingSuppression.Enter())
        {
            obj.StringValue = "suppressed";
        }

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void OnPropertyChanged_AfterSuppressionEnds_ShouldPublish()
    {
        // Arrange
        var obj = new TestCoreObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, obj, _sequenceGenerator);

        // Act
        using (PublishingSuppression.Enter())
        {
            obj.StringValue = "suppressed";
        }
        obj.StringValue = "not suppressed";

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<string>)receivedOperations[0];
        Assert.That(operation.NewValue, Is.EqualTo("not suppressed"));
    }

    [Test]
    public void OnPropertyChanged_WithNotTrackedProperty_ShouldNotPublish()
    {
        // Arrange
        var obj = new TestCoreObjectWithNotTrackedProperty();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, obj, _sequenceGenerator);

        // Act
        obj.NotTrackedValue = "changed";

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void OnPropertyChanged_WithTrackedProperty_ShouldPublish()
    {
        // Arrange
        var obj = new TestCoreObjectWithNotTrackedProperty();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, obj, _sequenceGenerator);

        // Act
        obj.TrackedValue = "changed";

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    [Test]
    public void OnPropertyChanged_WhenPropertyNotInPropertyPathsToTrack_ShouldNotPublish()
    {
        // Arrange
        var obj = new TestCoreObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        var propertyPathsToTrack = new HashSet<string> { "StringValue" };

        using var operationObserver = new CoreObjectOperationObserver(
            testObserver, obj, _sequenceGenerator, "", propertyPathsToTrack);

        // Act
        obj.IntValue = 42;

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void OnPropertyChanged_WithNewChildObject_ShouldCreateNewChildPublisher()
    {
        // Arrange
        var parentObj = new TestParentCoreObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, parentObj, _sequenceGenerator);
        receivedOperations.Clear();

        // Act
        var newChild = new TestCoreObject { StringValue = "new child" };
        parentObj.Child = newChild;

        // First operation is for setting Child property
        receivedOperations.Clear();

        // Modify new child
        newChild.StringValue = "modified";

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<string>)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("Child.StringValue"));
    }

    [Test]
    public void OnPropertyChanged_WithReplacedChildObject_ShouldDisposeOldPublisher()
    {
        // Arrange
        var oldChild = new TestCoreObject { StringValue = "old" };
        var parentObj = new TestParentCoreObject { Child = oldChild };
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, parentObj, _sequenceGenerator);

        // Replace child
        var newChild = new TestCoreObject { StringValue = "new" };
        parentObj.Child = newChild;
        receivedOperations.Clear();

        // Act
        oldChild.StringValue = "should not be tracked";

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void OnPropertyChanged_WithNewList_ShouldCreateNewCollectionPublisher()
    {
        // Arrange
        var parentObj = new TestCollectionCoreObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, parentObj, _sequenceGenerator);
        receivedOperations.Clear();

        // Act
        var newList = new CoreList<TestCoreObject>();
        var item = new TestCoreObject { StringValue = "new item" };
        newList.Add(item);
        parentObj.Items = newList;

        // Clear operations from property change
        receivedOperations.Clear();

        // Add item to new list
        var anotherItem = new TestCoreObject { StringValue = "another" };
        parentObj.Items.Add(anotherItem);

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    [Test]
    public void OnPropertyChanged_MultipleSequentialChanges_ShouldPublishAllOperations()
    {
        // Arrange
        var obj = new TestCoreObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, obj, _sequenceGenerator);

        // Act
        obj.StringValue = "first";
        obj.StringValue = "second";
        obj.StringValue = "third";

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(3));
        var operations = receivedOperations.Cast<UpdatePropertyValueOperation<string>>().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(operations[0].NewValue, Is.EqualTo("first"));
            Assert.That(operations[1].NewValue, Is.EqualTo("second"));
            Assert.That(operations[2].NewValue, Is.EqualTo("third"));
        });
    }

    [Test]
    public void OnPropertyChanged_ShouldIncrementSequenceNumber()
    {
        // Arrange
        var obj = new TestCoreObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, obj, _sequenceGenerator);

        // Act
        obj.StringValue = "first";
        obj.StringValue = "second";

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(2));
        Assert.That(receivedOperations[1].SequenceNumber, Is.GreaterThan(receivedOperations[0].SequenceNumber));
    }

    #endregion

    #region KeyFrame SplineEasing Tests

    [Test]
    public void Constructor_WithKeyFrameAndSplineEasing_ShouldInitializeSplineEasingPublisher()
    {
        // Arrange
        var keyFrame = new KeyFrame<float>
        {
            Value = 1.0f,
            KeyTime = TimeSpan.FromSeconds(1),
            Easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f)
        };

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        // Act
        using var operationObserver = new CoreObjectOperationObserver(testObserver, keyFrame, _sequenceGenerator);

        var splineEasing = (SplineEasing)keyFrame.Easing;
        splineEasing.X1 = 0.5f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    [Test]
    public void OnPropertyChanged_WhenEasingChangedToSplineEasing_ShouldCreateNewSplinePublisher()
    {
        // Arrange
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

        // Act
        var newSplineEasing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        keyFrame.Easing = newSplineEasing;

        // Clear operation from Easing property change
        receivedOperations.Clear();

        newSplineEasing.X1 = 0.5f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    [Test]
    public void OnPropertyChanged_WhenEasingChangedFromSplineEasing_ShouldDisposeOldSplinePublisher()
    {
        // Arrange
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

        // Act
        keyFrame.Easing = new LinearEasing();
        receivedOperations.Clear();

        oldSplineEasing.X1 = 0.5f;

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    #endregion

    #region Operations Property Tests

    [Test]
    public void Operations_ShouldReturnObservable()
    {
        // Arrange
        var obj = new TestCoreObject();

        // Act
        using var observer = new CoreObjectOperationObserver(null, obj, _sequenceGenerator);

        // Assert
        Assert.That(observer.Operations, Is.InstanceOf<IObservable<ChangeOperation>>());
    }

    [Test]
    public void Operations_ShouldAllowMultipleSubscribers()
    {
        // Arrange
        var obj = new TestCoreObject();
        var receivedOperations1 = new List<ChangeOperation>();
        var receivedOperations2 = new List<ChangeOperation>();

        using var operationObserver = new CoreObjectOperationObserver(null, obj, _sequenceGenerator);

        // Act
        using var sub1 = operationObserver.Operations.Subscribe(op => receivedOperations1.Add(op));
        using var sub2 = operationObserver.Operations.Subscribe(op => receivedOperations2.Add(op));

        obj.StringValue = "changed";

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(receivedOperations1, Has.Count.EqualTo(1));
            Assert.That(receivedOperations2, Has.Count.EqualTo(1));
        });
    }

    #endregion

    #region BuildPropertyPath Tests

    [Test]
    public void BuildPropertyPath_WithEmptyPath_ShouldReturnPropertyName()
    {
        // Arrange
        var obj = new TestCoreObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        // Act
        using var operationObserver = new CoreObjectOperationObserver(testObserver, obj, _sequenceGenerator, "");
        obj.StringValue = "test";

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<string>)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("StringValue"));
    }

    #endregion

    #region Deep Nesting Tests

    [Test]
    public void Constructor_WithDeepNestedObjects_ShouldTrackAllLevels()
    {
        // Arrange
        var innerChild = new TestCoreObject { StringValue = "inner" };
        var middleChild = new TestParentCoreObject { Child = innerChild };
        var parentObj = new TestGrandParentCoreObject { MiddleChild = middleChild };

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        // Act
        using var operationObserver = new CoreObjectOperationObserver(testObserver, parentObj, _sequenceGenerator);
        innerChild.StringValue = "modified";

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<string>)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("MiddleChild.Child.StringValue"));
    }

    [Test]
    public void Dispose_WithDeepNestedObjects_ShouldStopAllTracking()
    {
        // Arrange
        var innerChild = new TestCoreObject { StringValue = "inner" };
        var middleChild = new TestParentCoreObject { Child = innerChild };
        var parentObj = new TestGrandParentCoreObject { MiddleChild = middleChild };

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var operationObserver = new CoreObjectOperationObserver(testObserver, parentObj, _sequenceGenerator);

        // Act
        operationObserver.Dispose();
        innerChild.StringValue = "should not track";

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    #endregion

    #region PropertyPathsToTrack Advanced Tests

    [Test]
    public void Constructor_WithNestedPropertyPathsToTrack_ShouldOnlyTrackSpecified()
    {
        // Arrange
        var child = new TestCoreObject();
        var parent = new TestParentCoreObject { Child = child };

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        var propertyPathsToTrack = new HashSet<string> { "Child.StringValue" };

        // Act
        using var operationObserver = new CoreObjectOperationObserver(
            testObserver, parent, _sequenceGenerator, "", propertyPathsToTrack);

        child.StringValue = "tracked";
        child.IntValue = 100;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<string>)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("Child.StringValue"));
    }

    #endregion

    #region Hierarchical Tests

    [Test]
    public void OnPropertyChanged_WithHierarchicalObject_ShouldNotTrackHierarchicalParent()
    {
        // Arrange
        var parent = new TestHierarchicalObject();
        var child = new TestHierarchicalObject();

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        // Create observer for child
        using var operationObserver = new CoreObjectOperationObserver(testObserver, child, _sequenceGenerator);

        // Act - simulate hierarchy attachment (internally sets HierarchicalParent)
        parent.AddTestChild(child);

        // Assert - HierarchicalParent changes should not be published
        var hierarchicalParentOps = receivedOperations
            .Where(op => op is IPropertyPathProvider ppp && ppp.PropertyPath == "HierarchicalParent")
            .ToList();
        Assert.That(hierarchicalParentOps, Is.Empty);
    }

    [Test]
    public void OnPropertyChanged_WithHierarchicalObject_ShouldTrackOtherProperties()
    {
        // Arrange
        var obj = new TestHierarchicalObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, obj, _sequenceGenerator);

        // Act
        obj.Title = "changed";

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<string>)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("Title"));
    }

    #endregion

    #region EngineObject Tests

    [Test]
    public void Constructor_WithEngineObject_ShouldInitializeEnginePropertyPublishers()
    {
        // Arrange
        var engineObj = new TestEngineObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        // Act
        using var operationObserver = new CoreObjectOperationObserver(testObserver, engineObj, _sequenceGenerator);
        engineObj.FloatValue.CurrentValue = 50f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    [Test]
    public void Dispose_WithEngineObject_ShouldDisposeEnginePropertyPublishers()
    {
        // Arrange
        var engineObj = new TestEngineObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var operationObserver = new CoreObjectOperationObserver(testObserver, engineObj, _sequenceGenerator);

        // Act
        operationObserver.Dispose();
        engineObj.FloatValue.CurrentValue = 50f;

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void OnPropertyChanged_WithEngineObject_ShouldPublishEnginePropertyChanges()
    {
        // Arrange
        var engineObj = new TestEngineObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, engineObj, _sequenceGenerator);

        // Act
        engineObj.FloatValue.CurrentValue = 25f;
        engineObj.FloatValue.CurrentValue = 75f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(2));
    }

    [Test]
    public void OnPropertyChanged_WithEngineObjectAndPublishingSuppressed_ShouldNotPublish()
    {
        // Arrange
        var engineObj = new TestEngineObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, engineObj, _sequenceGenerator);

        // Act
        using (PublishingSuppression.Enter())
        {
            engineObj.FloatValue.CurrentValue = 50f;
        }

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Constructor_WithEngineObjectAndPropertyPathsToTrack_ShouldOnlyTrackSpecified()
    {
        // Arrange
        var engineObj = new TestEngineObject();
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        // Note: For EngineObject properties, the path must include sub-properties like "FloatValue.CurrentValue"
        // because the filter logic checks for sub-path containment
        var propertyPathsToTrack = new HashSet<string> { "FloatValue.CurrentValue" };

        // Act
        using var operationObserver = new CoreObjectOperationObserver(
            testObserver, engineObj, _sequenceGenerator, "", propertyPathsToTrack);

        engineObj.FloatValue.CurrentValue = 50f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    #endregion

    #region NodeItem Tests

    [Test]
    public void Constructor_WithNodeItem_ShouldInitializeNodeItemPublisher()
    {
        // Arrange
        var nodeItem = new DefaultNodeItem<float>();
        var adapter = new NodePropertyAdapter<float>("TestProperty", 0f, null);
        nodeItem.SetProperty(adapter);

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        // Act
        using var operationObserver = new CoreObjectOperationObserver(testObserver, nodeItem, _sequenceGenerator);
        adapter.SetValue(50f);

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    [Test]
    public void Dispose_WithNodeItem_ShouldDisposeNodeItemPublisher()
    {
        // Arrange
        var nodeItem = new DefaultNodeItem<float>();
        var adapter = new NodePropertyAdapter<float>("TestProperty", 0f, null);
        nodeItem.SetProperty(adapter);

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var operationObserver = new CoreObjectOperationObserver(testObserver, nodeItem, _sequenceGenerator);

        // Act
        operationObserver.Dispose();
        adapter.SetValue(50f);

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void OnPropertyChanged_WithNodeItem_ShouldPublishPropertyValueChanges()
    {
        // Arrange
        var nodeItem = new DefaultNodeItem<float>();
        var adapter = new NodePropertyAdapter<float>("TestProperty", 0f, null);
        nodeItem.SetProperty(adapter);

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, nodeItem, _sequenceGenerator);

        // Act
        adapter.SetValue(25f);
        adapter.SetValue(75f);

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(2));
    }

    [Test]
    public void OnPropertyChanged_WithNodeItemAndPublishingSuppressed_ShouldNotPublish()
    {
        // Arrange
        var nodeItem = new DefaultNodeItem<float>();
        var adapter = new NodePropertyAdapter<float>("TestProperty", 0f, null);
        nodeItem.SetProperty(adapter);

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, nodeItem, _sequenceGenerator);

        // Act
        using (PublishingSuppression.Enter())
        {
            adapter.SetValue(50f);
        }

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void OnPropertyChanged_WithNodeItemAfterSuppressionEnds_ShouldPublish()
    {
        // Arrange
        var nodeItem = new DefaultNodeItem<float>();
        var adapter = new NodePropertyAdapter<float>("TestProperty", 0f, null);
        nodeItem.SetProperty(adapter);

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, nodeItem, _sequenceGenerator);

        // Act
        using (PublishingSuppression.Enter())
        {
            adapter.SetValue(25f);
        }
        adapter.SetValue(75f);

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    [Test]
    public void Constructor_WithNodeItemAndAnimation_ShouldTrackAnimationChanges()
    {
        // Arrange
        var animation = new KeyFrameAnimation<float>();
        var nodeItem = new DefaultNodeItem<float>();
        var adapter = new NodePropertyAdapter<float>("TestProperty", 0f, animation);
        nodeItem.SetProperty(adapter);

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, nodeItem, _sequenceGenerator);

        // Act - Change the animation
        var newAnimation = new KeyFrameAnimation<float>();
        adapter.Animation = newAnimation;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    [Test]
    public void OnPropertyChanged_WithNodeItemAnimationChange_ShouldPublishAnimationChange()
    {
        // Arrange
        var nodeItem = new DefaultNodeItem<float>();
        var adapter = new NodePropertyAdapter<float>("TestProperty", 0f, null);
        nodeItem.SetProperty(adapter);

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new CoreObjectOperationObserver(testObserver, nodeItem, _sequenceGenerator);

        // Act
        adapter.Animation = new KeyFrameAnimation<float>();

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    [Test]
    public void Constructor_WithNodeItemAndPropertyPathsToTrack_ShouldOnlyTrackSpecified()
    {
        // Arrange
        var nodeItem = new DefaultNodeItem<float>();
        var adapter = new NodePropertyAdapter<float>("TestProperty", 0f, null);
        nodeItem.SetProperty(adapter);

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        var propertyPathsToTrack = new HashSet<string> { "Property" };

        // Act
        using var operationObserver = new CoreObjectOperationObserver(
            testObserver, nodeItem, _sequenceGenerator, "", propertyPathsToTrack);

        adapter.SetValue(50f);

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    [Test]
    public void Constructor_WithNodeItemAndPropertyPathsToTrackExcludingProperty_ShouldNotTrack()
    {
        // Arrange
        var nodeItem = new DefaultNodeItem<float>();
        var adapter = new NodePropertyAdapter<float>("TestProperty", 0f, null);
        nodeItem.SetProperty(adapter);

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        // Only track Animation, not Property
        var propertyPathsToTrack = new HashSet<string> { "Animation" };

        // Act
        using var operationObserver = new CoreObjectOperationObserver(
            testObserver, nodeItem, _sequenceGenerator, "", propertyPathsToTrack);

        adapter.SetValue(50f);

        // Assert - Property changes should not be tracked
        var propertyOps = receivedOperations.Where(op =>
            op is IPropertyPathProvider ppp && ppp.PropertyPath == "Property").ToList();
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
                .SetAttribute(new NotTrackedAttribute())
                .Register();

            TrackedValueProperty = ConfigureProperty<string?, TestCoreObjectWithNotTrackedProperty>(nameof(TrackedValue))
                .Accessor(o => o.TrackedValue, (o, v) => o.TrackedValue = v)
                .Register();
        }

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
        public IProperty<float> FloatValue { get; } = Property.CreateAnimatable(0f);
    }

    #endregion
}
