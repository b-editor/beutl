using System.Reactive;
using Beutl.Animation;
using Beutl.Collections;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Operations;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Editor;

public class EnginePropertyOperationObserverTests
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
        var engineObj = new TestEngineObjectWithFloat();
        var property = engineObj.FloatValue;

        // Act
        using var observer = new EnginePropertyOperationObserver<float>(
            null, engineObj, property, _sequenceGenerator, "FloatValue");

        // Assert
        Assert.That(observer.Operations, Is.Not.Null);
    }

    [Test]
    public void Constructor_WithObserver_ShouldSubscribeToOperations()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithFloat();
        var property = engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        // Act
        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");
        property.CurrentValue = 50f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        Assert.That(receivedOperations[0], Is.TypeOf<UpdatePropertyValueOperation<float>>());
    }

    [Test]
    public void Constructor_WithNullObserver_ShouldNotThrow()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithFloat();
        var property = engineObj.FloatValue;

        // Act & Assert
        Assert.DoesNotThrow(() =>
        {
            using var observer = new EnginePropertyOperationObserver<float>(
                null, engineObj, property, _sequenceGenerator, "FloatValue");
        });
    }

    [Test]
    public void Constructor_WithPropertyPath_ShouldBuildCorrectPath()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithFloat();
        var property = engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        // Act
        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "Parent.FloatValue");
        property.CurrentValue = 25f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<float>)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("Parent.FloatValue"));
    }

    [Test]
    public void Constructor_WithCoreObjectValue_ShouldInitializeChildPublisher()
    {
        // Arrange
        var childObj = new TestChildCoreObject { Title = "child" };
        var engineObj = new TestEngineObjectWithCoreObject(childObj);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        // Act
        using var observer = new EnginePropertyOperationObserver<TestChildCoreObject>(
            testObserver, engineObj, engineObj.ChildProperty, _sequenceGenerator, "Child");
        childObj.Title = "modified";

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<string>)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("Child.Title"));
    }

    [Test]
    public void Constructor_WithListValue_ShouldInitializeCollectionPublisher()
    {
        // Arrange
        var list = new CoreList<TestChildCoreObject>();
        var item = new TestChildCoreObject { Title = "item" };
        list.Add(item);
        var engineObj = new TestEngineObjectWithList(list);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        // Act
        using var observer = new EnginePropertyOperationObserver<CoreList<TestChildCoreObject>>(
            testObserver, engineObj, engineObj.ItemsProperty, _sequenceGenerator, "Items");
        item.Title = "modified";

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    [Test]
    public void Constructor_WithAnimatablePropertyAndAnimation_ShouldInitializeAnimationPublisher()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var animation = new KeyFrameAnimation<float>();
        property.Animation = animation;

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        // Act
        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        // Modify animation (add a keyframe)
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 1.0f, KeyTime = TimeSpan.FromSeconds(1) });

        // Assert
        Assert.That(receivedOperations, Has.Count.GreaterThanOrEqualTo(1));
    }

    #endregion

    #region Value Change Tests

    [Test]
    public void OnValueChanged_ShouldPublishUpdatePropertyValueOperation()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithFloat();
        var property = engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        // Act
        property.CurrentValue = 75f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<float>)receivedOperations[0];
        Assert.Multiple(() =>
        {
            Assert.That(operation.PropertyPath, Is.EqualTo("FloatValue"));
            Assert.That(operation.NewValue, Is.EqualTo(75f));
            Assert.That(operation.OldValue, Is.EqualTo(0f));
        });
    }

    [Test]
    public void OnValueChanged_WithPublishingSuppression_ShouldNotPublish()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithFloat();
        var property = engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        // Act
        using (PublishingSuppression.Enter())
        {
            property.CurrentValue = 50f;
        }

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void OnValueChanged_AfterSuppressionEnds_ShouldPublish()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithFloat();
        var property = engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        // Act
        using (PublishingSuppression.Enter())
        {
            property.CurrentValue = 25f;
        }
        property.CurrentValue = 75f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<float>)receivedOperations[0];
        Assert.That(operation.NewValue, Is.EqualTo(75f));
    }

    [Test]
    public void OnValueChanged_WithNewCoreObject_ShouldCreateNewChildPublisher()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithCoreObject(null!);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<TestChildCoreObject>(
            testObserver, engineObj, engineObj.ChildProperty, _sequenceGenerator, "Child");

        // Act
        var newChild = new TestChildCoreObject { Title = "new" };
        engineObj.ChildProperty.CurrentValue = newChild;
        receivedOperations.Clear();

        newChild.Title = "modified";

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<string>)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("Child.Title"));
    }

    [Test]
    public void OnValueChanged_WithReplacedCoreObject_ShouldDisposeOldPublisher()
    {
        // Arrange
        var oldChild = new TestChildCoreObject { Title = "old" };
        var engineObj = new TestEngineObjectWithCoreObject(oldChild);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<TestChildCoreObject>(
            testObserver, engineObj, engineObj.ChildProperty, _sequenceGenerator, "Child");

        // Replace child
        var newChild = new TestChildCoreObject { Title = "new" };
        engineObj.ChildProperty.CurrentValue = newChild;
        receivedOperations.Clear();

        // Act
        oldChild.Title = "should not be tracked";

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void OnValueChanged_MultipleSequentialChanges_ShouldPublishAllOperations()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithFloat();
        var property = engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        // Act
        property.CurrentValue = 10f;
        property.CurrentValue = 20f;
        property.CurrentValue = 30f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(3));
        var operations = receivedOperations.Cast<UpdatePropertyValueOperation<float>>().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(operations[0].NewValue, Is.EqualTo(10f));
            Assert.That(operations[1].NewValue, Is.EqualTo(20f));
            Assert.That(operations[2].NewValue, Is.EqualTo(30f));
        });
    }

    #endregion

    #region Animation Change Tests

    [Test]
    public void OnAnimationChanged_ShouldPublishUpdatePropertyValueOperation()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        // Act
        var newAnimation = new KeyFrameAnimation<float>();
        property.Animation = newAnimation;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = receivedOperations[0] as UpdatePropertyValueOperation<IAnimation<float>>;
        Assert.That(operation, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(operation!.PropertyPath, Is.EqualTo("FloatValue.Animation"));
            Assert.That(operation.NewValue, Is.SameAs(newAnimation));
            Assert.That(operation.OldValue, Is.Null);
        });
    }

    [Test]
    public void OnAnimationChanged_WithPublishingSuppression_ShouldNotPublish()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        // Act
        using (PublishingSuppression.Enter())
        {
            property.Animation = new KeyFrameAnimation<float>();
        }

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void OnAnimationChanged_ShouldCreateNewAnimationPublisher()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        var newAnimation = new KeyFrameAnimation<float>();
        property.Animation = newAnimation;
        receivedOperations.Clear();

        // Act - Modify the animation
        newAnimation.KeyFrames.Add(new KeyFrame<float> { Value = 1.0f, KeyTime = TimeSpan.FromSeconds(1) });

        // Assert
        Assert.That(receivedOperations, Has.Count.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void OnAnimationChanged_ShouldDisposeOldAnimationPublisher()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var oldAnimation = new KeyFrameAnimation<float>();
        property.Animation = oldAnimation;

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        // Replace animation
        var newAnimation = new KeyFrameAnimation<float>();
        property.Animation = newAnimation;
        receivedOperations.Clear();

        // Act - Modify old animation
        oldAnimation.KeyFrames.Add(new KeyFrame<float> { Value = 1.0f, KeyTime = TimeSpan.FromSeconds(1) });

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void OnAnimationChanged_ToNull_ShouldPublishAndDisposePublisher()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var animation = new KeyFrameAnimation<float>();
        property.Animation = animation;

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");
        receivedOperations.Clear();

        // Act
        property.Animation = null;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = receivedOperations[0] as UpdatePropertyValueOperation<IAnimation<float>>;
        Assert.That(operation, Is.Not.Null);
        Assert.That(operation!.NewValue, Is.Null);
    }

    #endregion

    #region Expression Change Tests

    [Test]
    public void OnExpressionChanged_ShouldPublishUpdatePropertyValueOperation()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        // Act
        var expression = new TestExpression<float>(50f);
        property.Expression = expression;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = receivedOperations[0] as UpdatePropertyValueOperation<IExpression<float>>;
        Assert.That(operation, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(operation!.PropertyPath, Is.EqualTo("FloatValue.Expression"));
            Assert.That(operation.NewValue, Is.SameAs(expression));
            Assert.That(operation.OldValue, Is.Null);
        });
    }

    [Test]
    public void OnExpressionChanged_WithPublishingSuppression_ShouldNotPublish()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        // Act
        using (PublishingSuppression.Enter())
        {
            property.Expression = new TestExpression<float>(50f);
        }

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void OnExpressionChanged_ToNull_ShouldPublish()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        property.Expression = new TestExpression<float>(50f);

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");
        receivedOperations.Clear();

        // Act
        property.Expression = null;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = receivedOperations[0] as UpdatePropertyValueOperation<IExpression<float>>;
        Assert.That(operation, Is.Not.Null);
        Assert.That(operation!.NewValue, Is.Null);
    }

    [Test]
    public void OnExpressionChanged_Replacement_ShouldPublishWithOldAndNewValues()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var oldExpression = new TestExpression<float>(25f);
        property.Expression = oldExpression;

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");
        receivedOperations.Clear();

        // Act
        var newExpression = new TestExpression<float>(75f);
        property.Expression = newExpression;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = receivedOperations[0] as UpdatePropertyValueOperation<IExpression<float>>;
        Assert.That(operation, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(operation!.NewValue, Is.SameAs(newExpression));
            Assert.That(operation.OldValue, Is.SameAs(oldExpression));
        });
    }

    #endregion

    #region Dispose Tests

    [Test]
    public void Dispose_ShouldUnsubscribeFromValueChanged()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithFloat();
        var property = engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        // Act
        observer.Dispose();
        property.CurrentValue = 50f;

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Dispose_WithAnimatableProperty_ShouldUnsubscribeFromAnimationChanged()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        // Act
        observer.Dispose();
        property.Animation = new KeyFrameAnimation<float>();

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Dispose_WithAnimatableProperty_ShouldUnsubscribeFromExpressionChanged()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        // Act
        observer.Dispose();
        property.Expression = new TestExpression<float>(50f);

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Dispose_ShouldCompleteOperationsObservable()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithFloat();
        var property = engineObj.FloatValue;
        bool completed = false;

        var observer = new EnginePropertyOperationObserver<float>(
            null, engineObj, property, _sequenceGenerator, "FloatValue");
        using var subscription = observer.Operations.Subscribe(_ => { }, () => completed = true);

        // Act
        observer.Dispose();

        // Assert
        Assert.That(completed, Is.True);
    }

    [Test]
    public void Dispose_ShouldDisposeValuePublisher()
    {
        // Arrange
        var childObj = new TestChildCoreObject { Title = "child" };
        var engineObj = new TestEngineObjectWithCoreObject(childObj);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var observer = new EnginePropertyOperationObserver<TestChildCoreObject>(
            testObserver, engineObj, engineObj.ChildProperty, _sequenceGenerator, "Child");

        // Act
        observer.Dispose();
        childObj.Title = "modified";

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Dispose_ShouldDisposeAnimationPublisher()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var animation = new KeyFrameAnimation<float>();
        property.Animation = animation;

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");
        receivedOperations.Clear();

        // Act
        observer.Dispose();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 1.0f, KeyTime = TimeSpan.FromSeconds(1) });

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    #endregion

    #region PropertyPathsToTrack Tests

    [Test]
    public void Constructor_WithPropertyPathsToTrack_FilteringCurrentValue_ShouldNotTrackValue()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithFloat();
        var property = engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        // Only track Animation, not CurrentValue
        var propertyPathsToTrack = new HashSet<string> { "FloatValue.Animation" };

        // Act
        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue", propertyPathsToTrack);

        property.CurrentValue = 50f;

        // Assert
        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Constructor_WithPropertyPathsToTrack_FilteringAnimation_ShouldNotTrackAnimation()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        // Only track CurrentValue, not Animation
        var propertyPathsToTrack = new HashSet<string> { "FloatValue.CurrentValue" };

        // Act
        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue", propertyPathsToTrack);

        property.Animation = new KeyFrameAnimation<float>();

        // Assert
        var animationOps = receivedOperations.Where(op =>
            op is IPropertyPathProvider ppp && ppp.PropertyPath.Contains("Animation")).ToList();
        Assert.That(animationOps, Is.Empty);
    }

    [Test]
    public void Constructor_WithPropertyPathsToTrack_FilteringExpression_ShouldNotTrackExpression()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        // Only track CurrentValue, not Expression
        var propertyPathsToTrack = new HashSet<string> { "FloatValue.CurrentValue" };

        // Act
        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue", propertyPathsToTrack);

        property.Expression = new TestExpression<float>(50f);

        // Assert
        var expressionOps = receivedOperations.Where(op =>
            op is IPropertyPathProvider ppp && ppp.PropertyPath.Contains("Expression")).ToList();
        Assert.That(expressionOps, Is.Empty);
    }

    [Test]
    public void Constructor_WithNullPropertyPathsToTrack_ShouldTrackEverything()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        // Act
        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue", null);

        property.CurrentValue = 50f;
        property.Animation = new KeyFrameAnimation<float>();
        property.Expression = new TestExpression<float>(75f);

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(3));
    }

    #endregion

    #region Sequence Number Tests

    [Test]
    public void OnValueChanged_ShouldIncrementSequenceNumber()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithFloat();
        var property = engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        // Act
        property.CurrentValue = 25f;
        property.CurrentValue = 50f;
        property.CurrentValue = 75f;

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(3));
        Assert.That(receivedOperations[1].SequenceNumber, Is.GreaterThan(receivedOperations[0].SequenceNumber));
        Assert.That(receivedOperations[2].SequenceNumber, Is.GreaterThan(receivedOperations[1].SequenceNumber));
    }

    [Test]
    public void OnAnimationChanged_ShouldIncrementSequenceNumber()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        // Act
        property.Animation = new KeyFrameAnimation<float>();
        property.Animation = new KeyFrameAnimation<float>();

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(2));
        Assert.That(receivedOperations[1].SequenceNumber, Is.GreaterThan(receivedOperations[0].SequenceNumber));
    }

    [Test]
    public void OnExpressionChanged_ShouldIncrementSequenceNumber()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        // Act
        property.Expression = new TestExpression<float>(25f);
        property.Expression = new TestExpression<float>(50f);

        // Assert
        Assert.That(receivedOperations, Has.Count.EqualTo(2));
        Assert.That(receivedOperations[1].SequenceNumber, Is.GreaterThan(receivedOperations[0].SequenceNumber));
    }

    #endregion

    #region Operations Property Tests

    [Test]
    public void Operations_ShouldReturnObservable()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithFloat();
        var property = engineObj.FloatValue;

        // Act
        using var observer = new EnginePropertyOperationObserver<float>(
            null, engineObj, property, _sequenceGenerator, "FloatValue");

        // Assert
        Assert.That(observer.Operations, Is.InstanceOf<IObservable<ChangeOperation>>());
    }

    [Test]
    public void Operations_ShouldAllowMultipleSubscribers()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithFloat();
        var property = engineObj.FloatValue;
        var receivedOperations1 = new List<ChangeOperation>();
        var receivedOperations2 = new List<ChangeOperation>();

        using var observer = new EnginePropertyOperationObserver<float>(
            null, engineObj, property, _sequenceGenerator, "FloatValue");

        // Act
        using var sub1 = observer.Operations.Subscribe(op => receivedOperations1.Add(op));
        using var sub2 = observer.Operations.Subscribe(op => receivedOperations2.Add(op));

        property.CurrentValue = 50f;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(receivedOperations1, Has.Count.EqualTo(1));
            Assert.That(receivedOperations2, Has.Count.EqualTo(1));
        });
    }

    #endregion

    #region Non-AnimatableProperty Tests

    [Test]
    public void Constructor_WithNonAnimatableProperty_ShouldNotSubscribeToAnimationChanged()
    {
        // Arrange
        var engineObj = new TestEngineObjectWithFloat();
        var property = engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        // Act & Assert - Should not throw even though property doesn't have Animation/Expression
        Assert.DoesNotThrow(() =>
        {
            using var observer = new EnginePropertyOperationObserver<float>(
                testObserver, engineObj, property, _sequenceGenerator, "FloatValue");
            property.CurrentValue = 50f;
        });
    }

    #endregion

    #region Test Helper Classes

    private class TestChildCoreObject : CoreObject
    {
        public static readonly CoreProperty<string?> TitleProperty;

        private string? _title;

        static TestChildCoreObject()
        {
            TitleProperty = ConfigureProperty<string?, TestChildCoreObject>(nameof(Title))
                .Accessor(o => o.Title, (o, v) => o.Title = v)
                .Register();
        }

        public string? Title
        {
            get => _title;
            set => SetAndRaise(TitleProperty, ref _title, value);
        }
    }

    [SuppressResourceClassGeneration]
    private class TestEngineObjectWithFloat : EngineObject
    {
        public TestEngineObjectWithFloat()
        {
            FloatValue = Property.Create(0f);
            ScanProperties<TestEngineObjectWithFloat>();
        }

        public IProperty<float> FloatValue { get; }
    }

    [SuppressResourceClassGeneration]
    private class TestEngineObjectWithAnimatableFloat : EngineObject
    {
        public TestEngineObjectWithAnimatableFloat()
        {
            FloatValue = Property.CreateAnimatable(0f);
            ScanProperties<TestEngineObjectWithAnimatableFloat>();
        }

        public IProperty<float> FloatValue { get; }
    }

    [SuppressResourceClassGeneration]
    private class TestEngineObjectWithCoreObject : EngineObject
    {
        public TestEngineObjectWithCoreObject(TestChildCoreObject? initialValue)
        {
            ChildProperty = Property.Create(initialValue)!;
            ScanProperties<TestEngineObjectWithCoreObject>();
        }

        public IProperty<TestChildCoreObject> ChildProperty { get; }
    }

    [SuppressResourceClassGeneration]
    private class TestEngineObjectWithList : EngineObject
    {
        public TestEngineObjectWithList(CoreList<TestChildCoreObject>? initialValue)
        {
            ItemsProperty = Property.Create(initialValue)!;
            ScanProperties<TestEngineObjectWithList>();
        }

        public IProperty<CoreList<TestChildCoreObject>> ItemsProperty { get; }
    }

    private class TestExpression<T> : IExpression<T>
    {
        private readonly T _value;

        public TestExpression(T value)
        {
            _value = value;
        }

        public Type ResultType => typeof(T);

        public string ExpressionString => _value?.ToString() ?? "";

        public T Evaluate(ExpressionContext context)
        {
            return _value;
        }

        public bool Validate(out string? error)
        {
            error = null;
            return true;
        }
    }

    #endregion
}
