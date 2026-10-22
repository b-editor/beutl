using Beutl.Animation;
using Beutl.Collections;
using Beutl.Editor;
using Beutl.Editor.Operations;
using Beutl.Engine;
using Beutl.Engine.Expressions;

namespace Beutl.UnitTests.Editor;

public class UpdatePropertyValueOperationTests
{
    private TestCoreObject _root = null!;
    private OperationSequenceGenerator _sequenceGenerator = null!;

    [SetUp]
    public void Setup()
    {
        _root = new TestCoreObject();
        _sequenceGenerator = new OperationSequenceGenerator();
    }

    private UpdatePropertyValueOperation<T> CreateOp<T>(CoreObject obj, string propertyPath, T newValue, T oldValue)
    {
        return new UpdatePropertyValueOperation<T>(obj, propertyPath, newValue, oldValue)
        {
            SequenceNumber = _sequenceGenerator.GetNext()
        };
    }

    #region Constructor Tests

    [Test]
    public void Constructor_ShouldSetObjectPathAndValues()
    {
        var op = CreateOp(_root, "Value", 10, 5);

        Assert.Multiple(() =>
        {
            Assert.That(op.Object, Is.SameAs(_root));
            Assert.That(op.PropertyPath, Is.EqualTo("Value"));
            Assert.That(op.NewValue, Is.EqualTo(10));
            Assert.That(op.OldValue, Is.EqualTo(5));
        });
    }

    #endregion

    #region Interface Implementation Tests

    [Test]
    public void ShouldImplementIUpdatePropertyValueOperation()
    {
        var op = CreateOp(_root, "Value", 10, 5);

        var iface = (IUpdatePropertyValueOperation)op;
        Assert.Multiple(() =>
        {
            Assert.That(iface.Object, Is.SameAs(_root));
            Assert.That(iface.PropertyPath, Is.EqualTo("Value"));
            Assert.That(iface.NewValue, Is.EqualTo(10));
            Assert.That(iface.OldValue, Is.EqualTo(5));
        });
    }

    #endregion

    #region Apply Tests - ValueOnlyProperty

    [Test]
    public void Apply_WithValueOnlyProperty_ShouldSetNewValue()
    {
        _root.Value = 0;
        var op = CreateOp(_root, "Value", 42, 0);

        op.Apply();

        Assert.That(_root.Value, Is.EqualTo(42));
    }

    [Test]
    public void Apply_WithStringValueOnlyProperty_ShouldSetNewValue()
    {
        _root.Title = "original";
        var op = CreateOp<string?>(_root, "Title", "modified", "original");

        op.Apply();

        Assert.That(_root.Title, Is.EqualTo("modified"));
    }

    [Test]
    public void Apply_WithNullableProperty_ShouldSetToNull()
    {
        _root.Title = "original";
        var op = CreateOp<string?>(_root, "Title", null, "original");

        op.Apply();

        Assert.That(_root.Title, Is.Null);
    }

    [Test]
    public void Apply_WithNullableProperty_ShouldSetFromNull()
    {
        _root.Title = null;
        var op = CreateOp<string?>(_root, "Title", "new value", null);

        op.Apply();

        Assert.That(_root.Title, Is.EqualTo("new value"));
    }

    #endregion

    #region Revert Tests - ValueOnlyProperty

    [Test]
    public void Revert_WithStringValueOnlyProperty_ShouldSetOldValue()
    {
        _root.Title = "modified";
        var op = CreateOp<string?>(_root, "Title", "modified", "original");

        op.Revert();

        Assert.That(_root.Title, Is.EqualTo("original"));
    }

    [Test]
    public void Revert_WithNullableProperty_ShouldSetToNull()
    {
        _root.Title = "value";
        var op = CreateOp<string?>(_root, "Title", "value", null);

        op.Revert();

        Assert.That(_root.Title, Is.Null);
    }

    [Test]
    public void ApplyThenRevert_WithValueOnlyProperty_ShouldRestoreOriginalValue()
    {
        _root.Value = 10;
        var op = CreateOp(_root, "Value", 50, 10);

        op.Apply();
        Assert.That(_root.Value, Is.EqualTo(50));
        op.Revert();

        Assert.That(_root.Value, Is.EqualTo(10));
    }

    #endregion

    #region Apply Tests - EngineProperty

    [Test]
    public void Apply_WithEngineProperty_ShouldSetNewValue()
    {
        var engineObj = new TestEngineObject();
        engineObj.FloatValue.Value = 0f;
        var op = CreateOp<float>(engineObj, "FloatValue", 50f, 0f);

        op.Apply();

        Assert.That(engineObj.FloatValue.Value, Is.EqualTo(50f));
    }

    [Test]
    public void Apply_WithEnginePropertyAnimation_ShouldSetAnimation()
    {
        var engineObj = new TestAnimatableEngineObject();
        var animation = new KeyFrameAnimation<float>();
        var op = CreateOp<KeyFrameAnimation<float>?>(engineObj, "FloatValue.Animation", animation, null);

        op.Apply();

        Assert.That(engineObj.FloatValue.GetKeyFrameAnimation(), Is.SameAs(animation));
    }

    [Test]
    public void Apply_WithEnginePropertyExpression_ShouldSetExpression()
    {
        var engineObj = new TestAnimatableEngineObject();
        var expression = new TestExpression<float>(100f);
        var op = CreateOp<IExpression<float>?>(engineObj, "FloatValue.Expression", expression, null);

        op.Apply();

        Assert.That(engineObj.FloatValue.Expression, Is.SameAs(expression));
    }

    [Test]
    public void Apply_WithNestedPropertyPath_ShouldParseCorrectly()
    {
        // Tests that "Parent.Child.FloatValue" correctly identifies "FloatValue" as the property name
        var engineObj = new TestEngineObject();
        var op = CreateOp<float>(engineObj, "Parent.Child.FloatValue", 75f, 0f);

        op.Apply();

        Assert.That(engineObj.FloatValue.Value, Is.EqualTo(75f));
    }

    [Test]
    public void Apply_WithNonExistentEngineProperty_ShouldThrowInvalidOperationException()
    {
        var engineObj = new TestEngineObject();
        var op = CreateOp<float>(engineObj, "NonExistentProperty", 50f, 0f);

        Assert.Throws<InvalidOperationException>(() => op.Apply());
    }

    #endregion

    #region Revert Tests - EngineProperty

    [Test]
    public void Revert_WithEnginePropertyAnimation_ShouldSetOldAnimation()
    {
        var engineObj = new TestAnimatableEngineObject();
        var newAnimation = new KeyFrameAnimation<float>();
        var oldAnimation = new KeyFrameAnimation<float>();
        engineObj.FloatValue.SetKeyFrameAnimation(newAnimation);
        var op = CreateOp<KeyFrameAnimation<float>?>(engineObj, "FloatValue.Animation", newAnimation, oldAnimation);

        op.Revert();

        Assert.That(engineObj.FloatValue.GetKeyFrameAnimation(), Is.SameAs(oldAnimation));
    }

    [Test]
    public void Revert_WithEnginePropertyAnimation_ShouldSetToNull()
    {
        var engineObj = new TestAnimatableEngineObject();
        var animation = new KeyFrameAnimation<float>();
        engineObj.FloatValue.SetKeyFrameAnimation(animation);
        var op = CreateOp<KeyFrameAnimation<float>?>(engineObj, "FloatValue.Animation", animation, null);

        op.Revert();

        Assert.That(engineObj.FloatValue.GetKeyFrameAnimation(), Is.Null);
    }

    [Test]
    public void Revert_WithEnginePropertyExpression_ShouldSetOldExpression()
    {
        var engineObj = new TestAnimatableEngineObject();
        var newExpression = new TestExpression<float>(100f);
        var oldExpression = new TestExpression<float>(50f);
        engineObj.FloatValue.Expression = newExpression;
        var op = CreateOp<IExpression<float>?>(engineObj, "FloatValue.Expression", newExpression, oldExpression);

        op.Revert();

        Assert.That(engineObj.FloatValue.Expression, Is.SameAs(oldExpression));
    }

    [Test]
    public void ApplyThenRevert_WithEngineProperty_ShouldRestoreOriginalValue()
    {
        var engineObj = new TestEngineObject();
        engineObj.FloatValue.Value = 10f;
        var op = CreateOp<float>(engineObj, "FloatValue", 100f, 10f);

        op.Apply();
        Assert.That(engineObj.FloatValue.Value, Is.EqualTo(100f));
        op.Revert();

        Assert.That(engineObj.FloatValue.Value, Is.EqualTo(10f));
    }

    #endregion

    #region TryMerge Tests

    [Test]
    public void TryMerge_WithDifferentObject_ShouldReturnFalse()
    {
        var otherRoot = new TestCoreObject();
        var op1 = CreateOp(_root, "Value", 10, 0);
        var op2 = CreateOp(otherRoot, "Value", 20, 10);

        var result = op1.TryMerge(op2);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TryMerge_WithDifferentPropertyPath_ShouldReturnFalse()
    {
        var op1 = CreateOp(_root, "Value", 10, 0);
        var op2 = CreateOp(_root, "OtherValue", 20, 10);

        var result = op1.TryMerge(op2);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(op1.NewValue, Is.EqualTo(10));
            Assert.That(op1.PropertyPath, Is.EqualTo("Value"));
        });
    }

    [Test]
    public void TryMerge_WithDifferentType_ShouldReturnFalse()
    {
        var op1 = CreateOp(_root, "Value", 10, 0);
        var op2 = CreateOp<string>(_root, "Value", "test", "");

        var result = op1.TryMerge(op2);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TryMerge_WithNonUpdatePropertyValueOperation_ShouldReturnFalse()
    {
        var op1 = CreateOp(_root, "Value", 10, 0);
        var customOp = new TestChangeOperation { SequenceNumber = _sequenceGenerator.GetNext() };

        var result = op1.TryMerge(customOp);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TryMerge_MultipleTimes_ShouldAccumulateNewValues()
    {
        var op1 = CreateOp(_root, "Value", 10, 0);
        var op2 = CreateOp(_root, "Value", 20, 10);
        var op3 = CreateOp(_root, "Value", 30, 20);

        op1.TryMerge(op2);
        op1.TryMerge(op3);

        Assert.Multiple(() =>
        {
            Assert.That(op1.NewValue, Is.EqualTo(30));
            Assert.That(op1.OldValue, Is.EqualTo(0));
        });
    }

    #endregion

    #region PropertyPath Parsing Tests

    [Test]
    public void Apply_WithDeeplyNestedAnimationPath_ShouldParseCorrectly()
    {
        // "Parent.Child.FloatValue.Animation" should identify "FloatValue" as property name
        var engineObj = new TestAnimatableEngineObject();
        var animation = new KeyFrameAnimation<float>();
        var op = CreateOp<KeyFrameAnimation<float>?>(engineObj, "Parent.Child.FloatValue.Animation", animation, null);

        op.Apply();

        Assert.That(engineObj.FloatValue.GetKeyFrameAnimation(), Is.SameAs(animation));
    }

    [Test]
    public void Apply_WithDeeplyNestedExpressionPath_ShouldParseCorrectly()
    {
        // "Parent.Child.FloatValue.Expression" should identify "FloatValue" as property name
        var engineObj = new TestAnimatableEngineObject();
        var expression = new TestExpression<float>(100f);
        var op = CreateOp<IExpression<float>?>(engineObj, "Parent.Child.FloatValue.Expression", expression, null);

        op.Apply();

        Assert.That(engineObj.FloatValue.Expression, Is.SameAs(expression));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void Apply_WithComplexType_ShouldSetReference()
    {
        var list = new CoreList<int> { 1, 2, 3 };
        var newList = new CoreList<int> { 4, 5, 6 };
        _root.Items = list;
        var op = CreateOp<CoreList<int>?>(_root, "Items", newList, list);

        op.Apply();

        Assert.That(_root.Items, Is.SameAs(newList));
    }

    [Test]
    public void Revert_WithComplexType_ShouldRestoreReference()
    {
        var oldList = new CoreList<int> { 1, 2, 3 };
        var newList = new CoreList<int> { 4, 5, 6 };
        _root.Items = newList;
        var op = CreateOp<CoreList<int>?>(_root, "Items", newList, oldList);

        op.Revert();

        Assert.That(_root.Items, Is.SameAs(oldList));
    }

    [Test]
    public void Apply_WithNonAnimatableProperty_AnimationPath_ShouldNotThrow()
    {
        // SimpleProperty (non-animatable) doesn't support Animation
        var engineObj = new TestEngineObject();
        var op = CreateOp<IKeyFrameAnimation?>(engineObj, "FloatValue.Animation", null, null);

        // Should not throw, but also shouldn't do anything
        Assert.DoesNotThrow(() => op.Apply());
    }

    #endregion

    #region Test Helper Classes

    private class TestCoreObject : CoreObject
    {
        public TestCoreObject()
        {
            ScanProperties<TestCoreObject>();
        }

        public IProperty<int> ValueProperty { get; } = new ValueOnlyProperty<int>(default);

        public IProperty<string?> TitleProperty { get; } = new ValueOnlyProperty<string?>(null);

        public IProperty<CoreList<int>?> ItemsProperty { get; } = new ValueOnlyProperty<CoreList<int>?>(null);

        public int Value
        {
            get => ValueProperty.Value;
            set => ValueProperty.Value = value;
        }

        public string? Title
        {
            get => TitleProperty.Value;
            set => TitleProperty.Value = value;
        }

        public CoreList<int>? Items
        {
            get => ItemsProperty.Value;
            set => ItemsProperty.Value = value;
        }
    }

    [SuppressResourceClassGeneration]
    private class TestEngineObject : EngineObject
    {
        public TestEngineObject()
        {
            FloatValue = Property.CreateWithExpression(0f);
            ScanProperties<TestEngineObject>();
        }

        public IExpressionProperty<float> FloatValue { get; }
    }

    [SuppressResourceClassGeneration]
    private class TestAnimatableEngineObject : EngineObject
    {
        public TestAnimatableEngineObject()
        {
            FloatValue = Property.CreateAnimatable(0f);
            ScanProperties<TestAnimatableEngineObject>();
        }

        public IExpressionProperty<float> FloatValue { get; }
    }

    private class TestChangeOperation : ChangeOperation
    {
        public override void Apply() { }
        public override void Revert() { }
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
