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

    #region Constructor Tests

    [Test]
    public void Constructor_WithPropertyPath_ShouldBuildCorrectPath()
    {
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, _parent, "Easing");
        easing.X1 = 0.5f;

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdateSplineEasingOperation)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("Easing.X1"));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Constructor_ShouldSetParentOnOperations(bool withParent)
    {
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        TestCoreObject? parent = withParent ? _parent : null;

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, parent);
        easing.X1 = 0.5f;

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdateSplineEasingOperation)receivedOperations[0];
        Assert.That(operation.Parent, Is.SameAs(parent));
    }

    #endregion

    #region Operations Property Tests

    [Test]
    public void Operations_ShouldAllowMultipleSubscribers()
    {
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations1 = new List<ChangeOperation>();
        var receivedOperations2 = new List<ChangeOperation>();

        using var operationObserver = new SplineEasingOperationObserver(null, easing, _sequenceGenerator, null);

        using var sub1 = operationObserver.Operations.Subscribe(op => receivedOperations1.Add(op));
        using var sub2 = operationObserver.Operations.Subscribe(op => receivedOperations2.Add(op));

        easing.X1 = 0.5f;

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
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);

        operationObserver.Dispose();
        easing.X1 = 0.5f;

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Dispose_ShouldCompleteOperationsObservable()
    {
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        bool completed = false;

        var operationObserver = new SplineEasingOperationObserver(null, easing, _sequenceGenerator, null);
        using var subscription = operationObserver.Operations.Subscribe(_ => { }, () => completed = true);

        operationObserver.Dispose();

        Assert.That(completed, Is.True);
    }

    #endregion

    #region OnEasingChanged Tests

    [TestCase("X1", 0.5f, 0.25f)]
    [TestCase("Y1", 0.5f, 0.1f)]
    [TestCase("X2", 0.75f, 0.25f)]
    [TestCase("Y2", 0.5f, 1.0f)]
    public void OnEasingChanged_ShouldPublishOperation(string propertyName, float newValue, float oldValue)
    {
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);

        switch (propertyName)
        {
            case "X1":
                easing.X1 = newValue;
                break;
            case "Y1":
                easing.Y1 = newValue;
                break;
            case "X2":
                easing.X2 = newValue;
                break;
            case "Y2":
                easing.Y2 = newValue;
                break;
        }

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdateSplineEasingOperation)receivedOperations[0];
        Assert.Multiple(() =>
        {
            Assert.That(operation.Easing, Is.SameAs(easing));
            Assert.That(operation.PropertyPath, Is.EqualTo(propertyName));
            Assert.That(operation.NewValue, Is.EqualTo(newValue));
            Assert.That(operation.OldValue, Is.EqualTo(oldValue));
        });
    }

    [Test]
    public void OnEasingChanged_WithPublishingSuppressed_ShouldNotPublish()
    {
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);

        using (PublishingSuppression.Enter())
        {
            easing.X1 = 0.5f;
            easing.Y1 = 0.5f;
            easing.X2 = 0.5f;
            easing.Y2 = 0.5f;
        }

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void OnEasingChanged_AfterSuppressionEnds_ShouldPublish()
    {
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);

        using (PublishingSuppression.Enter())
        {
            easing.X1 = 0.3f;
        }
        easing.X1 = 0.5f;

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdateSplineEasingOperation)receivedOperations[0];
        Assert.That(operation.NewValue, Is.EqualTo(0.5f));
    }

    [Test]
    public void OnEasingChanged_WhenValueNotChanged_ShouldNotPublish()
    {
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);

        // Set to the same value
        easing.X1 = 0.25f;

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void OnEasingChanged_WithMultipleChanges_ShouldPublishAllOperations()
    {
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);

        easing.X1 = 0.3f;
        easing.Y1 = 0.2f;
        easing.X2 = 0.7f;
        easing.Y2 = 0.8f;

        Assert.That(receivedOperations, Has.Count.EqualTo(4));
    }

    [Test]
    public void OnEasingChanged_ShouldUpdateInternalStateAfterPublishing()
    {
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);

        // Change X1 twice
        easing.X1 = 0.5f;
        easing.X1 = 0.75f;

        Assert.That(receivedOperations, Has.Count.EqualTo(2));
        var firstOp = (UpdateSplineEasingOperation)receivedOperations[0];
        var secondOp = (UpdateSplineEasingOperation)receivedOperations[1];
        Assert.Multiple(() =>
        {
            Assert.That(firstOp.OldValue, Is.EqualTo(0.25f));
            Assert.That(firstOp.NewValue, Is.EqualTo(0.5f));
            Assert.That(secondOp.OldValue, Is.EqualTo(0.5f));
            Assert.That(secondOp.NewValue, Is.EqualTo(0.75f));
            Assert.That(secondOp.SequenceNumber, Is.GreaterThan(firstOp.SequenceNumber));
        });
    }

    #endregion

    #region ShouldTrack Tests

    [TestCase("", new[] { "X1" }, new[] { "X1" })]
    [TestCase("", new[] { "X1", "Y2" }, new[] { "X1", "Y2" })]
    [TestCase("", new string[0], new string[0])]
    [TestCase("Easing", new[] { "Easing.X1", "Easing.Y1" }, new[] { "Easing.X1", "Easing.Y1" })]
    [TestCase("Parent.Easing", new[] { "Parent.Easing.X1" }, new[] { "Parent.Easing.X1" })]
    public void OnEasingChanged_ShouldPublishOnlyTrackedProperties(
        string propertyPath, string[] propertyPathsToTrack, string[] expectedPaths)
    {
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null, propertyPath, new HashSet<string>(propertyPathsToTrack));

        easing.X1 = 0.5f;
        easing.Y1 = 0.5f;
        easing.X2 = 0.5f;
        easing.Y2 = 0.5f;

        Assert.That(
            receivedOperations.Cast<UpdateSplineEasingOperation>().Select(op => op.PropertyPath),
            Is.EqualTo(expectedPaths));
    }

    #endregion

    #region Integration Tests

    [Test]
    public void Integration_OperationApply_ShouldSetNewValue()
    {
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);

        easing.X1 = 0.5f;

        var operation = (UpdateSplineEasingOperation)receivedOperations[0];

        easing.X1 = 0.25f;

        using (PublishingSuppression.Enter())
        {
            operation.Apply();
        }

        Assert.That(easing.X1, Is.EqualTo(0.5f));
    }

    [Test]
    public void Integration_OperationRevert_ShouldSetOldValue()
    {
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);

        easing.X1 = 0.5f;

        var operation = (UpdateSplineEasingOperation)receivedOperations[0];

        using (PublishingSuppression.Enter())
        {
            operation.Revert();
        }

        Assert.That(easing.X1, Is.EqualTo(0.25f));
    }

    [Test]
    public void Integration_MultipleObservers_ShouldWorkIndependently()
    {
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations1 = new List<ChangeOperation>();
        var receivedOperations2 = new List<ChangeOperation>();
        var testObserver1 = Observer.Create<ChangeOperation>(op => receivedOperations1.Add(op));
        var testObserver2 = Observer.Create<ChangeOperation>(op => receivedOperations2.Add(op));

        using var operationObserver1 = new SplineEasingOperationObserver(
            testObserver1, easing, _sequenceGenerator, null);
        using var operationObserver2 = new SplineEasingOperationObserver(
            testObserver2, easing, new OperationSequenceGenerator(), null);

        easing.X1 = 0.5f;

        Assert.Multiple(() =>
        {
            Assert.That(receivedOperations1, Has.Count.EqualTo(1));
            Assert.That(receivedOperations2, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Integration_NestedSuppression_ShouldNotPublishUntilFullyExited()
    {
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var operationObserver = new SplineEasingOperationObserver(
            testObserver, easing, _sequenceGenerator, null);

        using (PublishingSuppression.Enter())
        {
            easing.X1 = 0.3f;
            using (PublishingSuppression.Enter())
            {
                easing.X1 = 0.4f;
            }
            easing.X1 = 0.5f;
        }

        Assert.That(receivedOperations, Is.Empty);
    }

    #endregion

    #region Test Helper Classes

    private class TestCoreObject : CoreObject
    {
    }

    #endregion
}
