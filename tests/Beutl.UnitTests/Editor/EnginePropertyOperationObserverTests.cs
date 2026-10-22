using System.Reactive;
using Beutl.Animation;
using Beutl.Collections;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Operations;
using Beutl.Engine;
using Beutl.Engine.Expressions;

namespace Beutl.UnitTests.Editor;

public class EnginePropertyOperationObserverTests
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
        var engineObj = new TestEngineObjectWithFloat();
        var property = engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "Parent.FloatValue");
        property.Value = 25f;

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<float>)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("Parent.FloatValue"));
    }

    [Test]
    public void Constructor_WithCoreObjectValue_ShouldInitializeChildPublisher()
    {
        var childObj = new TestChildCoreObject { Title = "child" };
        var engineObj = new TestEngineObjectWithCoreObject(childObj);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<TestChildCoreObject>(
            testObserver, engineObj, engineObj.ChildProperty, _sequenceGenerator, "Child");
        childObj.Title = "modified";

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<string>)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("Child.Title"));
    }

    [Test]
    public void Constructor_WithListValue_ShouldInitializeCollectionPublisher()
    {
        var list = new CoreList<TestChildCoreObject>();
        var item = new TestChildCoreObject { Title = "item" };
        list.Add(item);
        var engineObj = new TestEngineObjectWithList(list);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<CoreList<TestChildCoreObject>>(
            testObserver, engineObj, engineObj.ItemsProperty, _sequenceGenerator, "Items");
        item.Title = "modified";

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
    }

    [Test]
    public void Constructor_WithAnimatablePropertyAndAnimation_ShouldInitializeAnimationPublisher()
    {
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var animation = new KeyFrameAnimation<float>();
        property.SetKeyFrameAnimation(animation);

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        animation.KeyFrames.Add(new KeyFrame<float> { Value = 1.0f, KeyTime = TimeSpan.FromSeconds(1) });

        Assert.That(receivedOperations, Has.Count.GreaterThanOrEqualTo(1));
    }

    #endregion

    #region Value Change Tests

    [Test]
    public void OnValueChanged_ShouldPublishUpdatePropertyValueOperation()
    {
        var engineObj = new TestEngineObjectWithFloat();
        var property = engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        property.Value = 75f;

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
    public void OnValueChanged_AfterSuppressionEnds_ShouldPublish()
    {
        var engineObj = new TestEngineObjectWithFloat();
        var property = engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        using (PublishingSuppression.Enter())
        {
            property.Value = 25f;
        }
        property.Value = 75f;

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<float>)receivedOperations[0];
        Assert.That(operation.NewValue, Is.EqualTo(75f));
    }

    [Test]
    public void OnValueChanged_WithNewCoreObject_ShouldCreateNewChildPublisher()
    {
        var engineObj = new TestEngineObjectWithCoreObject(null!);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<TestChildCoreObject>(
            testObserver, engineObj, engineObj.ChildProperty, _sequenceGenerator, "Child");

        var newChild = new TestChildCoreObject { Title = "new" };
        engineObj.ChildProperty.Value = newChild;
        receivedOperations.Clear();

        newChild.Title = "modified";

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = (UpdatePropertyValueOperation<string>)receivedOperations[0];
        Assert.That(operation.PropertyPath, Is.EqualTo("Child.Title"));
    }

    [Test]
    public void OnValueChanged_WithReplacedCoreObject_ShouldDisposeOldPublisher()
    {
        var oldChild = new TestChildCoreObject { Title = "old" };
        var engineObj = new TestEngineObjectWithCoreObject(oldChild);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<TestChildCoreObject>(
            testObserver, engineObj, engineObj.ChildProperty, _sequenceGenerator, "Child");

        var newChild = new TestChildCoreObject { Title = "new" };
        engineObj.ChildProperty.Value = newChild;
        receivedOperations.Clear();

        oldChild.Title = "should not be tracked";

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void OnValueChanged_MultipleSequentialChanges_ShouldPublishAllOperations()
    {
        var engineObj = new TestEngineObjectWithFloat();
        var property = engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        property.Value = 10f;
        property.Value = 20f;
        property.Value = 30f;

        Assert.That(receivedOperations, Has.Count.EqualTo(3));
        var operations = receivedOperations.Cast<UpdatePropertyValueOperation<float>>().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(operations[0].NewValue, Is.EqualTo(10f));
            Assert.That(operations[1].NewValue, Is.EqualTo(20f));
            Assert.That(operations[2].NewValue, Is.EqualTo(30f));
            Assert.That(operations[1].SequenceNumber, Is.GreaterThan(operations[0].SequenceNumber));
            Assert.That(operations[2].SequenceNumber, Is.GreaterThan(operations[1].SequenceNumber));
        });
    }

    #endregion

    #region Animation Change Tests

    [Test]
    public void OnAnimationChanged_ShouldPublishUpdatePropertyValueOperation()
    {
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        var newAnimation = new KeyFrameAnimation<float>();
        property.SetKeyFrameAnimation(newAnimation);

        // Keyframes are the bottom of the expression chain, so setting them changes the chain.
        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = receivedOperations[0] as UpdatePropertyValueOperation<IExpression<float>>;
        Assert.That(operation, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(operation!.PropertyPath, Is.EqualTo("FloatValue.Expression"));
            Assert.That(operation.NewValue, Is.SameAs(newAnimation));
            Assert.That(operation.OldValue, Is.Null);
        });
    }

    [Test]
    public void OnAnimationChanged_WithPublishingSuppression_ShouldNotPublish()
    {
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        using (PublishingSuppression.Enter())
        {
            property.SetKeyFrameAnimation(new KeyFrameAnimation<float>());
        }

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void OnAnimationChanged_ShouldCreateNewAnimationPublisher()
    {
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        var newAnimation = new KeyFrameAnimation<float>();
        property.SetKeyFrameAnimation(newAnimation);
        receivedOperations.Clear();

        newAnimation.KeyFrames.Add(new KeyFrame<float> { Value = 1.0f, KeyTime = TimeSpan.FromSeconds(1) });

        Assert.That(receivedOperations, Has.Count.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void OnAnimationChanged_ShouldDisposeOldAnimationPublisher()
    {
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var oldAnimation = new KeyFrameAnimation<float>();
        property.SetKeyFrameAnimation(oldAnimation);

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        var newAnimation = new KeyFrameAnimation<float>();
        property.SetKeyFrameAnimation(newAnimation);
        receivedOperations.Clear();

        oldAnimation.KeyFrames.Add(new KeyFrame<float> { Value = 1.0f, KeyTime = TimeSpan.FromSeconds(1) });

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void OnAnimationChanged_ToNull_ShouldPublishAndDisposePublisher()
    {
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var animation = new KeyFrameAnimation<float>();
        property.SetKeyFrameAnimation(animation);

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");
        receivedOperations.Clear();

        property.SetKeyFrameAnimation(null);

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = receivedOperations[0] as UpdatePropertyValueOperation<IExpression<float>>;
        Assert.That(operation, Is.Not.Null);
        Assert.That((operation!.PropertyPath, operation.NewValue), Is.EqualTo(("FloatValue.Expression", (IExpression<float>?)null)));
    }

    #endregion

    #region Expression Change Tests

    [Test]
    public void OnExpressionChanged_ShouldPublishUpdatePropertyValueOperation()
    {
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        var expression = new TestExpression<float>(50f);
        property.Expression = expression;

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
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        using (PublishingSuppression.Enter())
        {
            property.Expression = new TestExpression<float>(50f);
        }

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void OnExpressionChanged_ToNull_ShouldPublish()
    {
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        property.Expression = new TestExpression<float>(50f);

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");
        receivedOperations.Clear();

        property.Expression = null;

        Assert.That(receivedOperations, Has.Count.EqualTo(1));
        var operation = receivedOperations[0] as UpdatePropertyValueOperation<IExpression<float>>;
        Assert.That(operation, Is.Not.Null);
        Assert.That(operation!.NewValue, Is.Null);
    }

    [Test]
    public void OnExpressionChanged_Replacement_ShouldPublishWithOldAndNewValues()
    {
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var oldExpression = new TestExpression<float>(25f);
        property.Expression = oldExpression;

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");
        receivedOperations.Clear();

        var newExpression = new TestExpression<float>(75f);
        property.Expression = newExpression;

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
        var engineObj = new TestEngineObjectWithFloat();
        var property = engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        observer.Dispose();
        property.Value = 50f;

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Dispose_WithAnimatableProperty_ShouldUnsubscribeFromAnimationChanged()
    {
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        observer.Dispose();
        property.SetKeyFrameAnimation(new KeyFrameAnimation<float>());

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Dispose_WithAnimatableProperty_ShouldUnsubscribeFromExpressionChanged()
    {
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        observer.Dispose();
        property.Expression = new TestExpression<float>(50f);

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Dispose_ShouldCompleteOperationsObservable()
    {
        var engineObj = new TestEngineObjectWithFloat();
        var property = engineObj.FloatValue;
        bool completed = false;

        var observer = new EnginePropertyOperationObserver<float>(
            null, engineObj, property, _sequenceGenerator, "FloatValue");
        using var subscription = observer.Operations.Subscribe(_ => { }, () => completed = true);

        observer.Dispose();

        Assert.That(completed, Is.True);
    }

    [Test]
    public void Dispose_ShouldDisposeValuePublisher()
    {
        var childObj = new TestChildCoreObject { Title = "child" };
        var engineObj = new TestEngineObjectWithCoreObject(childObj);
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var observer = new EnginePropertyOperationObserver<TestChildCoreObject>(
            testObserver, engineObj, engineObj.ChildProperty, _sequenceGenerator, "Child");

        observer.Dispose();
        childObj.Title = "modified";

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Dispose_ShouldDisposeAnimationPublisher()
    {
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var animation = new KeyFrameAnimation<float>();
        property.SetKeyFrameAnimation(animation);

        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");
        receivedOperations.Clear();

        observer.Dispose();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 1.0f, KeyTime = TimeSpan.FromSeconds(1) });

        Assert.That(receivedOperations, Is.Empty);
    }

    #endregion

    #region PropertyPathsToTrack Tests

    [Test]
    public void Constructor_WithPropertyPathsToTrack_FilteringCurrentValue_ShouldNotTrackValue()
    {
        var engineObj = new TestEngineObjectWithFloat();
        var property = engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        var propertyPathsToTrack = new HashSet<string> { "FloatValue.Animation" };

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue", propertyPathsToTrack);

        property.Value = 50f;

        Assert.That(receivedOperations, Is.Empty);
    }

    [Test]
    public void Constructor_WithPropertyPathsToTrack_FilteringAnimation_ShouldNotTrackAnimation()
    {
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        var propertyPathsToTrack = new HashSet<string> { "FloatValue.CurrentValue" };

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue", propertyPathsToTrack);

        property.SetKeyFrameAnimation(new KeyFrameAnimation<float>());

        var animationOps = receivedOperations.Where(op =>
            op is UpdatePropertyValueOperation<KeyFrameAnimation<float>> update && update.PropertyPath.Contains("Animation")).ToList();
        Assert.That(animationOps, Is.Empty);
    }

    [Test]
    public void Constructor_WithPropertyPathsToTrack_FilteringExpression_ShouldNotTrackExpression()
    {
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));
        var propertyPathsToTrack = new HashSet<string> { "FloatValue.CurrentValue" };

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue", propertyPathsToTrack);

        property.Expression = new TestExpression<float>(50f);

        var expressionOps = receivedOperations.Where(op =>
            op is UpdatePropertyValueOperation<IExpression<float>> update && update.PropertyPath.Contains("Expression")).ToList();
        Assert.That(expressionOps, Is.Empty);
    }

    [Test]
    public void Constructor_WithNullPropertyPathsToTrack_ShouldTrackEverything()
    {
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue", null);

        property.Value = 50f;
        property.SetKeyFrameAnimation(new KeyFrameAnimation<float>());
        property.Expression = new TestExpression<float>(75f);

        Assert.That(receivedOperations, Has.Count.EqualTo(3));
    }

    #endregion

    #region Sequence Number Tests

    [Test]
    public void OnAnimationChanged_ShouldIncrementSequenceNumber()
    {
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        property.SetKeyFrameAnimation(new KeyFrameAnimation<float>());
        property.SetKeyFrameAnimation(new KeyFrameAnimation<float>());

        Assert.That(receivedOperations, Has.Count.EqualTo(2));
        Assert.That(receivedOperations[1].SequenceNumber, Is.GreaterThan(receivedOperations[0].SequenceNumber));
    }

    [Test]
    public void OnExpressionChanged_ShouldIncrementSequenceNumber()
    {
        var engineObj = new TestEngineObjectWithAnimatableFloat();
        var property = (AnimatableProperty<float>)engineObj.FloatValue;
        var receivedOperations = new List<ChangeOperation>();
        var testObserver = Observer.Create<ChangeOperation>(op => receivedOperations.Add(op));

        using var observer = new EnginePropertyOperationObserver<float>(
            testObserver, engineObj, property, _sequenceGenerator, "FloatValue");

        property.Expression = new TestExpression<float>(25f);
        property.Expression = new TestExpression<float>(50f);

        Assert.That(receivedOperations, Has.Count.EqualTo(2));
        Assert.That(receivedOperations[1].SequenceNumber, Is.GreaterThan(receivedOperations[0].SequenceNumber));
    }

    #endregion

    #region Test Helper Classes

    private class TestChildCoreObject : CoreObject
    {
        public TestChildCoreObject()
        {
            ScanProperties<TestChildCoreObject>();
        }

        public IProperty<string?> TitleProperty { get; } = new ValueOnlyProperty<string?>(null);

        public string? Title
        {
            get => TitleProperty.Value;
            set => TitleProperty.Value = value;
        }
    }

    [SuppressResourceClassGeneration]
    private class TestEngineObjectWithFloat : EngineObject
    {
        public TestEngineObjectWithFloat()
        {
            FloatValue = Property.CreateExpression(0f);
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
            ChildProperty = Property.CreateExpression(initialValue)!;
            ScanProperties<TestEngineObjectWithCoreObject>();
        }

        public IProperty<TestChildCoreObject> ChildProperty { get; }
    }

    [SuppressResourceClassGeneration]
    private class TestEngineObjectWithList : EngineObject
    {
        public TestEngineObjectWithList(CoreList<TestChildCoreObject>? initialValue)
        {
            ItemsProperty = Property.CreateExpression(initialValue)!;
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
