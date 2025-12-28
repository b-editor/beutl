using System.Reactive;
using Beutl.Animation.Easings;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Operations;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Editor;

public class SplineEasingOperationObserverTests
{
    private OperationSequenceGenerator _sequenceGenerator = null!;
    private TestCoreObject _parent = null!;

    [SetUp]
    public void Setup()
    {
        Log.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
        _sequenceGenerator = new OperationSequenceGenerator();
        _parent = new TestCoreObject();
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
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);

        // Act
        using var observer = new SplineEasingOperationObserver(null, easing, _sequenceGenerator, null);

        // Assert
        Assert.That(observer.Operations, Is.Not.Null);
    }

    [Test]
    public void Constructor_WithObserver_ShouldSubscribeToOperations()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        // Act
        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);
        easing.X1 = 0.5f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        Assert.That(receivedOperations[0], Is.TypeOf<UpdateSplineEasingOperation>());
    }

    [Test]
    public void Constructor_WithNullObserver_ShouldNotThrow()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);

        // Act & Assert
        Assert.DoesNotThrow(() =>
        {
            using var observer = new SplineEasingOperationObserver(null, easing, _sequenceGenerator, null);
        });
    }

    [Test]
    public void Constructor_WithEmptyPropertyPath_ShouldWorkCorrectly()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);

        // Act
        using var observer = new SplineEasingOperationObserver(
            null, easing, _sequenceGenerator, null, "");

        // Assert
        Assert.That(observer.Operations, Is.Not.Null);
    }

    [Test]
    public void Constructor_WithPropertyPath_ShouldBuildCorrectPath()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        // Act
        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, _parent, "Easing");
        easing.X1 = 0.5f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdateSplineEasingOperation)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("Easing.X1"));
    }

    [Test]
    public void Constructor_WithPropertyPathsToTrack_ShouldOnlyTrackSpecifiedProperties()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        var propertyPathsToTrack = new HashSet<string> { "X1" };

        // Act
        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null, "", propertyPathsToTrack);

        easing.X1 = 0.5f;
        easing.Y1 = 0.5f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdateSplineEasingOperation)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("X1"));
    }

    [Test]
    public void Constructor_WithNestedPropertyPathsToTrack_ShouldParseCorrectly()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        var propertyPathsToTrack = new HashSet<string> { "Easing.X1", "Easing.Y1" };

        // Act
        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null, "Easing", propertyPathsToTrack);

        easing.X1 = 0.5f;
        easing.Y1 = 0.5f;
        easing.X2 = 0.5f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(2));
    }

    [Test]
    public void Constructor_ShouldCaptureInitialEasingValues()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        // Act
        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);

        // Change values
        easing.X1 = 0.5f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdateSplineEasingOperation)receivedOperations[0];
        Assert.Multiple(() =>
        {
            Assert.That(operation.OldValue, Is.EqualTo(0.25f));
            Assert.That(operation.NewValue, Is.EqualTo(0.5f));
        });
    }

    [Test]
    public void Constructor_WithParent_ShouldSetParentOnOperations()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        // Act
        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, _parent);
        easing.X1 = 0.5f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdateSplineEasingOperation)receivedOperations[0];
        Assert.That(operation.Parent, Is.SameAs(_parent));
    }

    [Test]
    public void Constructor_WithNullParent_ShouldSetNullParentOnOperations()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        // Act
        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);
        easing.X1 = 0.5f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdateSplineEasingOperation)receivedOperations[0];
        Assert.That(operation.Parent, Is.Null);
    }

    #endregion

    #region Operations Property Tests

    [Test]
    public void Operations_ShouldReturnObservable()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);

        // Act
        using var observer = new SplineEasingOperationObserver(null, easing, _sequenceGenerator, null);

        // Assert
        Assert.That(observer.Operations, Is.InstanceOf<IObservable<ChangeOperation>>());
    }

    [Test]
    public void Operations_ShouldAllowMultipleSubscribers()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations1 = new List<ChangeOperation>();
        var receivedOperations2 = new List<ChangeOperation>();

        using var operationObserver = new SplineEasingOperationObserver(null, easing, _sequenceGenerator, null);

        // Act
        using var sub1 = operationObserver.Operations.Subscribe(op => receivedOperations1.Add(op));
        using var sub2 = operationObserver.Operations.Subscribe(op => receivedOperations2.Add(op));

        easing.X1 = 0.5f;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(receivedOperations1, Has.Count.EqualTo(1));
            Assert.That(receivedOperations2, Has.Count.EqualTo(1));
        });
    }

    #endregion

    #region Dispose Tests

    [Test]
    public void Dispose_ShouldUnsubscribeFromChangedEvent()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);

        // Act
        operationObserver.Dispose();
        easing.X1 = 0.5f;

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Dispose_ShouldCompleteOperationsObservable()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        bool completed = false;

        var operationObserver = new SplineEasingOperationObserver(null, easing, _sequenceGenerator, null);
        using var subscription = operationObserver.Operations.Subscribe(_ => { }, () => completed = true);

        // Act
        operationObserver.Dispose();

        // Assert
        Assert.That(completed, Is.True);
    }

    [Test]
    public void Dispose_ShouldDisposeSubscription()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);

        // Act
        operationObserver.Dispose();

        // Attempt to use the Operations after dispose
        using var subscription = operationObserver.Operations.Subscribe(op => receivedOperations.Add(op));

        // Since it's completed, no more operations should be received
        // (but this won't throw)

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    #endregion

    #region OnEasingChanged Tests

    [Test]
    public void OnEasingChanged_WithX1Change_ShouldPublishOperation()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);

        // Act
        easing.X1 = 0.5f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdateSplineEasingOperation)receivedOperations[0];
        Assert.Multiple(() =>
        {
            Assert.That(operation.PropertyPath, Is.EqualTo("X1"));
            Assert.That(operation.NewValue, Is.EqualTo(0.5f));
            Assert.That(operation.OldValue, Is.EqualTo(0.25f));
        });
    }

    [Test]
    public void OnEasingChanged_WithY1Change_ShouldPublishOperation()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);

        // Act
        easing.Y1 = 0.5f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdateSplineEasingOperation)receivedOperations[0];
        Assert.Multiple(() =>
        {
            Assert.That(operation.PropertyPath, Is.EqualTo("Y1"));
            Assert.That(operation.NewValue, Is.EqualTo(0.5f));
            Assert.That(operation.OldValue, Is.EqualTo(0.1f));
        });
    }

    [Test]
    public void OnEasingChanged_WithX2Change_ShouldPublishOperation()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);

        // Act
        easing.X2 = 0.75f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdateSplineEasingOperation)receivedOperations[0];
        Assert.Multiple(() =>
        {
            Assert.That(operation.PropertyPath, Is.EqualTo("X2"));
            Assert.That(operation.NewValue, Is.EqualTo(0.75f));
            Assert.That(operation.OldValue, Is.EqualTo(0.25f));
        });
    }

    [Test]
    public void OnEasingChanged_WithY2Change_ShouldPublishOperation()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);

        // Act
        easing.Y2 = 0.5f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdateSplineEasingOperation)receivedOperations[0];
        Assert.Multiple(() =>
        {
            Assert.That(operation.PropertyPath, Is.EqualTo("Y2"));
            Assert.That(operation.NewValue, Is.EqualTo(0.5f));
            Assert.That(operation.OldValue, Is.EqualTo(1.0f));
        });
    }

    [Test]
    public void OnEasingChanged_WithPublishingSuppressed_ShouldNotPublish()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);

        // Act
        using (PublishingSuppression.Enter())
        {
            easing.X1 = 0.5f;
            easing.Y1 = 0.5f;
            easing.X2 = 0.5f;
            easing.Y2 = 0.5f;
        }

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void OnEasingChanged_AfterSuppressionEnds_ShouldPublish()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);

        // Act
        using (PublishingSuppression.Enter())
        {
            easing.X1 = 0.3f;
        }
        easing.X1 = 0.5f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdateSplineEasingOperation)receivedOperations[0];
        Assert.That(operation.NewValue, Is.EqualTo(0.5f));
    }

    [Test]
    public void OnEasingChanged_WhenValueNotChanged_ShouldNotPublish()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);

        // Act - Set to the same value
        easing.X1 = 0.25f;

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void OnEasingChanged_WithMultipleChanges_ShouldPublishAllOperations()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);

        // Act
        easing.X1 = 0.3f;
        easing.Y1 = 0.2f;
        easing.X2 = 0.7f;
        easing.Y2 = 0.8f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(4));
    }

    [Test]
    public void OnEasingChanged_ShouldIncrementSequenceNumber()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);

        // Act
        easing.X1 = 0.3f;
        easing.X1 = 0.4f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(2));
        Assert.That(receivedOperations[1].SequenceNumber, Is.GreaterThan(receivedOperations[0].SequenceNumber));
    }

    [Test]
    public void OnEasingChanged_ShouldUpdateInternalStateAfterPublishing()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);

        // Act - Change X1 twice
        easing.X1 = 0.5f;
        easing.X1 = 0.75f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(2));
        var firstOp = (UpdateSplineEasingOperation)receivedOperations[0];
        var secondOp = (UpdateSplineEasingOperation)receivedOperations[1];
        Assert.Multiple(() =>
        {
            Assert.That(firstOp.OldValue, Is.EqualTo(0.25f));
            Assert.That(firstOp.NewValue, Is.EqualTo(0.5f));
            Assert.That(secondOp.OldValue, Is.EqualTo(0.5f));
            Assert.That(secondOp.NewValue, Is.EqualTo(0.75f));
        });
    }

    #endregion

    #region ShouldTrack Tests

    [Test]
    public void ShouldTrack_WithNullPropertyPathsToTrack_ShouldTrackAllProperties()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null, "", null);

        // Act
        easing.X1 = 0.5f;
        easing.Y1 = 0.5f;
        easing.X2 = 0.5f;
        easing.Y2 = 0.5f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(4));
    }

    [Test]
    public void ShouldTrack_WithPropertyInTrackList_ShouldTrack()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        var propertyPathsToTrack = new HashSet<string> { "X1", "Y2" };

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null, "", propertyPathsToTrack);

        // Act
        easing.X1 = 0.5f;
        easing.Y2 = 0.5f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(2));
    }

    [Test]
    public void ShouldTrack_WithPropertyNotInTrackList_ShouldNotTrack()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        var propertyPathsToTrack = new HashSet<string> { "X1" };

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null, "", propertyPathsToTrack);

        // Act
        easing.Y1 = 0.5f;
        easing.X2 = 0.5f;
        easing.Y2 = 0.5f;

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void ShouldTrack_WithEmptyPropertyPathsToTrack_ShouldNotTrackAnyProperty()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        var propertyPathsToTrack = new HashSet<string>();

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null, "", propertyPathsToTrack);

        // Act
        easing.X1 = 0.5f;
        easing.Y1 = 0.5f;
        easing.X2 = 0.5f;
        easing.Y2 = 0.5f;

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void ShouldTrack_WithNestedPropertyPath_ShouldParseAndTrack()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        // Simulating nested path like "Parent.Easing.X1"
        var propertyPathsToTrack = new HashSet<string> { "Parent.Easing.X1" };

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null, "Parent.Easing", propertyPathsToTrack);

        // Act
        easing.X1 = 0.5f;
        easing.Y1 = 0.5f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdateSplineEasingOperation)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("Parent.Easing.X1"));
    }

    #endregion

    #region PublishChange Tests

    [Test]
    public void PublishChange_WithEmptyPropertyPath_ShouldUsePropertyNameOnly()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null, "");

        // Act
        easing.X1 = 0.5f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdateSplineEasingOperation)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("X1"));
    }

    [Test]
    public void PublishChange_WithPropertyPath_ShouldBuildFullPath()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null, "KeyFrame.Easing");

        // Act
        easing.X1 = 0.5f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdateSplineEasingOperation)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("KeyFrame.Easing.X1"));
    }

    [Test]
    public void PublishChange_ShouldSetCorrectEasingReference()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);

        // Act
        easing.X1 = 0.5f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdateSplineEasingOperation)receivedOperations[0];
        Assert.That(operation.Easing, Is.SameAs(easing));
    }

    [Test]
    public void PublishChange_ShouldSetSequenceNumber()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);

        // Act
        easing.X1 = 0.5f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdateSplineEasingOperation)receivedOperations[0];
        Assert.That(operation.SequenceNumber, Is.GreaterThan(0));
    }

    #endregion

    #region Integration Tests

    [Test]
    public void Integration_OperationApply_ShouldSetNewValue()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);

        easing.X1 = 0.5f;

        var operation = (UpdateSplineEasingOperation)receivedOperations[0];

        // Reset the value
        easing.X1 = 0.25f;

        // Act
        using (PublishingSuppression.Enter())
        {
            operation.Apply(new OperationExecutionContext(_parent));
        }

        // Assert
        Assert.That(easing.X1, Is.EqualTo(0.5f));
    }

    [Test]
    public void Integration_OperationRevert_ShouldSetOldValue()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);

        easing.X1 = 0.5f;

        var operation = (UpdateSplineEasingOperation)receivedOperations[0];

        // Act
        using (PublishingSuppression.Enter())
        {
            operation.Revert(new OperationExecutionContext(_parent));
        }

        // Assert
        Assert.That(easing.X1, Is.EqualTo(0.25f));
    }

    [Test]
    public void Integration_MultipleObservers_ShouldWorkIndependently()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations1 = new List<ChangeOperation>();
        var receivedOperations2 = new List<ChangeOperation>();
        var testObserver1 = Observer.Create<ChangeOperation>(op => receivedOperations1.Add(op));
        var testObserver2 = Observer.Create<ChangeOperation>(op => receivedOperations2.Add(op));

        using var operationObserver1 = new SplineEasingOperationObserver(
            testObserver1, easing, _sequenceGenerator, null);
        using var operationObserver2 = new SplineEasingOperationObserver(
            testObserver2, easing, new OperationSequenceGenerator(), null);

        // Act
        easing.X1 = 0.5f;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(receivedOperations1, Has.Count.EqualTo(1));
            Assert.That(receivedOperations2, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Integration_NestedSuppression_ShouldNotPublishUntilFullyExited()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);

        // Act
        using (PublishingSuppression.Enter())
        {
            easing.X1 = 0.3f;
            using (PublishingSuppression.Enter())
            {
                easing.X1 = 0.4f;
            }
            easing.X1 = 0.5f;
        }

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    #endregion

    #region Test Helper Classes

    private class TestCoreObject : CoreObject
    {
    }

    #endregion
}
