using Beutl.Animation;
using Beutl.Collections;
using Beutl.Editor;
using Beutl.Editor.Operations;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Editor;

public class UpdatePropertyValueOperationTests
{
    private TestCoreObject _root = null!;
    private OperationExecutionContext _context = null!;
    private OperationSequenceGenerator _sequenceGenerator = null!;

    [SetUp]
    public void Setup()
    {
        Log.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
        _root = new TestCoreObject();
        _context = new OperationExecutionContext(_root);
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
    public void Constructor_ShouldSetObjectProperty()
    {
        // Arrange & Act
        var op = CreateOp(_root, "Value", 10, 0);

        // Assert
        Assert.That(op.Object, Is.SameAs(_root));
    }

    [Test]
    public void Constructor_ShouldSetPropertyPath()
    {
        // Arrange & Act
        var op = CreateOp(_root, "Value", 10, 0);

        // Assert
        Assert.That(op.PropertyPath, Is.EqualTo("Value"));
    }

    [Test]
    public void Constructor_ShouldSetNewValue()
    {
        // Arrange & Act
        var op = CreateOp(_root, "Value", 10, 0);

        // Assert
        Assert.That(op.NewValue, Is.EqualTo(10));
    }

    [Test]
    public void Constructor_ShouldSetOldValue()
    {
        // Arrange & Act
        var op = CreateOp(_root, "Value", 10, 5);

        // Assert
        Assert.That(op.OldValue, Is.EqualTo(5));
    }

    [Test]
    public void Constructor_WithComplexPropertyPath_ShouldSetPropertyPath()
    {
        // Arrange & Act
        var op = CreateOp(_root, "Parent.Child.Value", 10, 0);

        // Assert
        Assert.That(op.PropertyPath, Is.EqualTo("Parent.Child.Value"));
    }

    [Test]
    public void Constructor_ShouldInheritFromChangeOperation()
    {
        // Arrange & Act
        var op = CreateOp(_root, "Value", 10, 0);

        // Assert
        Assert.That(op, Is.InstanceOf<ChangeOperation>());
    }

    #endregion

    #region Property Setter Tests

    [Test]
    public void Object_ShouldBeSettable()
    {
        // Arrange
        var op = CreateOp(_root, "Value", 10, 0);
        var newRoot = new TestCoreObject();

        // Act
        op.Object = newRoot;

        // Assert
        Assert.That(op.Object, Is.SameAs(newRoot));
    }

    [Test]
    public void PropertyPath_ShouldBeSettable()
    {
        // Arrange
        var op = CreateOp(_root, "Value", 10, 0);

        // Act
        op.PropertyPath = "NewPropertyPath";

        // Assert
        Assert.That(op.PropertyPath, Is.EqualTo("NewPropertyPath"));
    }

    [Test]
    public void NewValue_ShouldBeSettable()
    {
        // Arrange
        var op = CreateOp(_root, "Value", 10, 0);

        // Act
        op.NewValue = 20;

        // Assert
        Assert.That(op.NewValue, Is.EqualTo(20));
    }

    [Test]
    public void OldValue_ShouldBeSettable()
    {
        // Arrange
        var op = CreateOp(_root, "Value", 10, 0);

        // Act
        op.OldValue = 5;

        // Assert
        Assert.That(op.OldValue, Is.EqualTo(5));
    }

    #endregion

    #region Interface Implementation Tests

    [Test]
    public void ShouldImplementIPropertyPathProvider()
    {
        // Arrange & Act
        var op = CreateOp(_root, "Value", 10, 0);

        // Assert
        Assert.That(op, Is.InstanceOf<IPropertyPathProvider>());
        Assert.That(((IPropertyPathProvider)op).PropertyPath, Is.EqualTo("Value"));
    }

    [Test]
    public void ShouldImplementIMergableChangeOperation()
    {
        // Arrange & Act
        var op = CreateOp(_root, "Value", 10, 0);

        // Assert
        Assert.That(op, Is.InstanceOf<IMergableChangeOperation>());
    }

    [Test]
    public void ShouldImplementIUpdatePropertyValueOperation()
    {
        // Arrange & Act
        var op = CreateOp(_root, "Value", 10, 5);

        // Assert
        Assert.That(op, Is.InstanceOf<IUpdatePropertyValueOperation>());
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

    #region Apply Tests - CoreProperty

    [Test]
    public void Apply_WithCoreProperty_ShouldSetNewValue()
    {
        // Arrange
        _root.Value = 0;
        var op = CreateOp(_root, "Value", 42, 0);

        // Act
        op.Apply(_context);

        // Assert
        Assert.That(_root.Value, Is.EqualTo(42));
    }

    [Test]
    public void Apply_WithCoreProperty_ShouldOverwriteExistingValue()
    {
        // Arrange
        _root.Value = 10;
        var op = CreateOp(_root, "Value", 100, 10);

        // Act
        op.Apply(_context);

        // Assert
        Assert.That(_root.Value, Is.EqualTo(100));
    }

    [Test]
    public void Apply_WithStringCoreProperty_ShouldSetNewValue()
    {
        // Arrange
        _root.Title = "original";
        var op = CreateOp<string?>(_root, "Title", "modified", "original");

        // Act
        op.Apply(_context);

        // Assert
        Assert.That(_root.Title, Is.EqualTo("modified"));
    }

    [Test]
    public void Apply_WithNullableProperty_ShouldSetToNull()
    {
        // Arrange
        _root.Title = "original";
        var op = CreateOp<string?>(_root, "Title", null, "original");

        // Act
        op.Apply(_context);

        // Assert
        Assert.That(_root.Title, Is.Null);
    }

    [Test]
    public void Apply_WithNullableProperty_ShouldSetFromNull()
    {
        // Arrange
        _root.Title = null;
        var op = CreateOp<string?>(_root, "Title", "new value", null);

        // Act
        op.Apply(_context);

        // Assert
        Assert.That(_root.Title, Is.EqualTo("new value"));
    }

    #endregion

    #region Revert Tests - CoreProperty

    [Test]
    public void Revert_WithCoreProperty_ShouldSetOldValue()
    {
        // Arrange
        _root.Value = 42;
        var op = CreateOp(_root, "Value", 42, 0);

        // Act
        op.Revert(_context);

        // Assert
        Assert.That(_root.Value, Is.EqualTo(0));
    }

    [Test]
    public void Revert_WithStringCoreProperty_ShouldSetOldValue()
    {
        // Arrange
        _root.Title = "modified";
        var op = CreateOp<string?>(_root, "Title", "modified", "original");

        // Act
        op.Revert(_context);

        // Assert
        Assert.That(_root.Title, Is.EqualTo("original"));
    }

    [Test]
    public void Revert_WithNullableProperty_ShouldSetToNull()
    {
        // Arrange
        _root.Title = "value";
        var op = CreateOp<string?>(_root, "Title", "value", null);

        // Act
        op.Revert(_context);

        // Assert
        Assert.That(_root.Title, Is.Null);
    }

    [Test]
    public void ApplyThenRevert_WithCoreProperty_ShouldRestoreOriginalValue()
    {
        // Arrange
        _root.Value = 10;
        var op = CreateOp(_root, "Value", 50, 10);

        // Act
        op.Apply(_context);
        Assert.That(_root.Value, Is.EqualTo(50)); // Verify apply worked
        op.Revert(_context);

        // Assert
        Assert.That(_root.Value, Is.EqualTo(10));
    }

    #endregion

    #region Apply Tests - EngineProperty

    [Test]
    public void Apply_WithEngineProperty_ShouldSetNewValue()
    {
        // Arrange
        var engineObj = new TestEngineObject();
        engineObj.FloatValue.CurrentValue = 0f;
        var context = new OperationExecutionContext(engineObj);
        var op = CreateOp<float>(engineObj, "FloatValue", 50f, 0f);

        // Act
        op.Apply(context);

        // Assert
        Assert.That(engineObj.FloatValue.CurrentValue, Is.EqualTo(50f));
    }

    [Test]
    public void Apply_WithEnginePropertyAnimation_ShouldSetAnimation()
    {
        // Arrange
        var engineObj = new TestAnimatableEngineObject();
        var animation = new KeyFrameAnimation<float>();
        var context = new OperationExecutionContext(engineObj);
        var op = CreateOp<IAnimation<float>?>(engineObj, "FloatValue.Animation", animation, null);

        // Act
        op.Apply(context);

        // Assert
        Assert.That(engineObj.FloatValue.Animation, Is.SameAs(animation));
    }

    [Test]
    public void Apply_WithEnginePropertyExpression_ShouldSetExpression()
    {
        // Arrange
        var engineObj = new TestAnimatableEngineObject();
        var expression = new TestExpression<float>(100f);
        var context = new OperationExecutionContext(engineObj);
        var op = CreateOp<IExpression<float>?>(engineObj, "FloatValue.Expression", expression, null);

        // Act
        op.Apply(context);

        // Assert
        Assert.That(engineObj.FloatValue.Expression, Is.SameAs(expression));
    }

    [Test]
    public void Apply_WithNestedPropertyPath_ShouldParseCorrectly()
    {
        // Arrange - Tests that "Parent.Child.FloatValue" correctly identifies "FloatValue" as the property name
        var engineObj = new TestEngineObject();
        var context = new OperationExecutionContext(engineObj);
        var op = CreateOp<float>(engineObj, "Parent.Child.FloatValue", 75f, 0f);

        // Act
        op.Apply(context);

        // Assert
        Assert.That(engineObj.FloatValue.CurrentValue, Is.EqualTo(75f));
    }

    [Test]
    public void Apply_WithNonExistentEngineProperty_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var engineObj = new TestEngineObject();
        var context = new OperationExecutionContext(engineObj);
        var op = CreateOp<float>(engineObj, "NonExistentProperty", 50f, 0f);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => op.Apply(context));
    }

    #endregion

    #region Revert Tests - EngineProperty

    [Test]
    public void Revert_WithEngineProperty_ShouldSetOldValue()
    {
        // Arrange
        var engineObj = new TestEngineObject();
        engineObj.FloatValue.CurrentValue = 50f;
        var context = new OperationExecutionContext(engineObj);
        var op = CreateOp<float>(engineObj, "FloatValue", 50f, 0f);

        // Act
        op.Revert(context);

        // Assert
        Assert.That(engineObj.FloatValue.CurrentValue, Is.EqualTo(0f));
    }

    [Test]
    public void Revert_WithEnginePropertyAnimation_ShouldSetOldAnimation()
    {
        // Arrange
        var engineObj = new TestAnimatableEngineObject();
        var newAnimation = new KeyFrameAnimation<float>();
        var oldAnimation = new KeyFrameAnimation<float>();
        engineObj.FloatValue.Animation = newAnimation;
        var context = new OperationExecutionContext(engineObj);
        var op = CreateOp<IAnimation<float>?>(engineObj, "FloatValue.Animation", newAnimation, oldAnimation);

        // Act
        op.Revert(context);

        // Assert
        Assert.That(engineObj.FloatValue.Animation, Is.SameAs(oldAnimation));
    }

    [Test]
    public void Revert_WithEnginePropertyAnimation_ShouldSetToNull()
    {
        // Arrange
        var engineObj = new TestAnimatableEngineObject();
        var animation = new KeyFrameAnimation<float>();
        engineObj.FloatValue.Animation = animation;
        var context = new OperationExecutionContext(engineObj);
        var op = CreateOp<IAnimation<float>?>(engineObj, "FloatValue.Animation", animation, null);

        // Act
        op.Revert(context);

        // Assert
        Assert.That(engineObj.FloatValue.Animation, Is.Null);
    }

    [Test]
    public void Revert_WithEnginePropertyExpression_ShouldSetOldExpression()
    {
        // Arrange
        var engineObj = new TestAnimatableEngineObject();
        var newExpression = new TestExpression<float>(100f);
        var oldExpression = new TestExpression<float>(50f);
        engineObj.FloatValue.Expression = newExpression;
        var context = new OperationExecutionContext(engineObj);
        var op = CreateOp<IExpression<float>?>(engineObj, "FloatValue.Expression", newExpression, oldExpression);

        // Act
        op.Revert(context);

        // Assert
        Assert.That(engineObj.FloatValue.Expression, Is.SameAs(oldExpression));
    }

    [Test]
    public void ApplyThenRevert_WithEngineProperty_ShouldRestoreOriginalValue()
    {
        // Arrange
        var engineObj = new TestEngineObject();
        engineObj.FloatValue.CurrentValue = 10f;
        var context = new OperationExecutionContext(engineObj);
        var op = CreateOp<float>(engineObj, "FloatValue", 100f, 10f);

        // Act
        op.Apply(context);
        Assert.That(engineObj.FloatValue.CurrentValue, Is.EqualTo(100f));
        op.Revert(context);

        // Assert
        Assert.That(engineObj.FloatValue.CurrentValue, Is.EqualTo(10f));
    }

    #endregion

    #region TryMerge Tests

    [Test]
    public void TryMerge_WithSameObjectAndPropertyPath_ShouldReturnTrue()
    {
        // Arrange
        var op1 = CreateOp(_root, "Value", 10, 0);
        var op2 = CreateOp(_root, "Value", 20, 10);

        // Act
        var result = op1.TryMerge(op2);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void TryMerge_ShouldUpdateNewValue()
    {
        // Arrange
        var op1 = CreateOp(_root, "Value", 10, 0);
        var op2 = CreateOp(_root, "Value", 20, 10);

        // Act
        op1.TryMerge(op2);

        // Assert
        Assert.That(op1.NewValue, Is.EqualTo(20));
    }

    [Test]
    public void TryMerge_ShouldPreserveOldValue()
    {
        // Arrange
        var op1 = CreateOp(_root, "Value", 10, 0);
        var op2 = CreateOp(_root, "Value", 20, 10);

        // Act
        op1.TryMerge(op2);

        // Assert
        Assert.That(op1.OldValue, Is.EqualTo(0)); // Original old value preserved
    }

    [Test]
    public void TryMerge_WithDifferentObject_ShouldReturnFalse()
    {
        // Arrange
        var otherRoot = new TestCoreObject();
        var op1 = CreateOp(_root, "Value", 10, 0);
        var op2 = CreateOp(otherRoot, "Value", 20, 10);

        // Act
        var result = op1.TryMerge(op2);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void TryMerge_WithDifferentPropertyPath_ShouldReturnFalse()
    {
        // Arrange
        var op1 = CreateOp(_root, "Value", 10, 0);
        var op2 = CreateOp(_root, "OtherValue", 20, 10);

        // Act
        var result = op1.TryMerge(op2);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void TryMerge_WithDifferentType_ShouldReturnFalse()
    {
        // Arrange
        var op1 = CreateOp(_root, "Value", 10, 0);
        var op2 = CreateOp<string>(_root, "Value", "test", "");

        // Act
        var result = op1.TryMerge(op2);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void TryMerge_WithNonUpdatePropertyValueOperation_ShouldReturnFalse()
    {
        // Arrange
        var op1 = CreateOp(_root, "Value", 10, 0);
        var customOp = new TestChangeOperation { SequenceNumber = _sequenceGenerator.GetNext() };

        // Act
        var result = op1.TryMerge(customOp);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void TryMerge_MultipleTimes_ShouldAccumulateNewValues()
    {
        // Arrange
        var op1 = CreateOp(_root, "Value", 10, 0);
        var op2 = CreateOp(_root, "Value", 20, 10);
        var op3 = CreateOp(_root, "Value", 30, 20);

        // Act
        op1.TryMerge(op2);
        op1.TryMerge(op3);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(op1.NewValue, Is.EqualTo(30));
            Assert.That(op1.OldValue, Is.EqualTo(0)); // Original old value preserved
        });
    }

    [Test]
    public void TryMerge_WithFailedMerge_ShouldNotModifyOriginal()
    {
        // Arrange
        var op1 = CreateOp(_root, "Value", 10, 0);
        var op2 = CreateOp(_root, "DifferentPath", 20, 10);

        // Act
        op1.TryMerge(op2);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(op1.NewValue, Is.EqualTo(10));
            Assert.That(op1.PropertyPath, Is.EqualTo("Value"));
        });
    }

    #endregion

    #region PropertyPath Parsing Tests

    [Test]
    public void Apply_WithSimplePropertyPath_ShouldFindProperty()
    {
        // Arrange
        var engineObj = new TestEngineObject();
        var context = new OperationExecutionContext(engineObj);
        var op = CreateOp<float>(engineObj, "FloatValue", 50f, 0f);

        // Act & Assert - Should not throw
        Assert.DoesNotThrow(() => op.Apply(context));
        Assert.That(engineObj.FloatValue.CurrentValue, Is.EqualTo(50f));
    }

    [Test]
    public void Apply_WithAnimationSuffixPropertyPath_ShouldSetAnimation()
    {
        // Arrange
        var engineObj = new TestAnimatableEngineObject();
        var animation = new KeyFrameAnimation<float>();
        var context = new OperationExecutionContext(engineObj);
        var op = CreateOp<IAnimation<float>?>(engineObj, "FloatValue.Animation", animation, null);

        // Act
        op.Apply(context);

        // Assert
        Assert.That(engineObj.FloatValue.Animation, Is.SameAs(animation));
    }

    [Test]
    public void Apply_WithExpressionSuffixPropertyPath_ShouldSetExpression()
    {
        // Arrange
        var engineObj = new TestAnimatableEngineObject();
        var expression = new TestExpression<float>(100f);
        var context = new OperationExecutionContext(engineObj);
        var op = CreateOp<IExpression<float>?>(engineObj, "FloatValue.Expression", expression, null);

        // Act
        op.Apply(context);

        // Assert
        Assert.That(engineObj.FloatValue.Expression, Is.SameAs(expression));
    }

    [Test]
    public void Apply_WithDeeplyNestedAnimationPath_ShouldParseCorrectly()
    {
        // Arrange - "Parent.Child.FloatValue.Animation" should identify "FloatValue" as property name
        var engineObj = new TestAnimatableEngineObject();
        var animation = new KeyFrameAnimation<float>();
        var context = new OperationExecutionContext(engineObj);
        var op = CreateOp<IAnimation<float>?>(engineObj, "Parent.Child.FloatValue.Animation", animation, null);

        // Act
        op.Apply(context);

        // Assert
        Assert.That(engineObj.FloatValue.Animation, Is.SameAs(animation));
    }

    [Test]
    public void Apply_WithDeeplyNestedExpressionPath_ShouldParseCorrectly()
    {
        // Arrange - "Parent.Child.FloatValue.Expression" should identify "FloatValue" as property name
        var engineObj = new TestAnimatableEngineObject();
        var expression = new TestExpression<float>(100f);
        var context = new OperationExecutionContext(engineObj);
        var op = CreateOp<IExpression<float>?>(engineObj, "Parent.Child.FloatValue.Expression", expression, null);

        // Act
        op.Apply(context);

        // Assert
        Assert.That(engineObj.FloatValue.Expression, Is.SameAs(expression));
    }

    #endregion

    #region SequenceNumber Tests

    [Test]
    public void SequenceNumber_ShouldBeInheritedFromChangeOperation()
    {
        // Arrange & Act
        var op = CreateOp(_root, "Value", 10, 0);
        var originalSeq = op.SequenceNumber;
        op.SequenceNumber = 42;

        // Assert
        Assert.That(op.SequenceNumber, Is.EqualTo(42));
    }

    [Test]
    public void SequenceNumber_ShouldBeSettable()
    {
        // Arrange
        var op = CreateOp(_root, "Value", 10, 0);

        // Act
        op.SequenceNumber = 100;

        // Assert
        Assert.That(op.SequenceNumber, Is.EqualTo(100));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void Apply_WithSameOldAndNewValue_ShouldStillSetValue()
    {
        // Arrange
        _root.Value = 10;
        var op = CreateOp(_root, "Value", 10, 10);

        // Act
        op.Apply(_context);

        // Assert
        Assert.That(_root.Value, Is.EqualTo(10));
    }

    [Test]
    public void Apply_WithComplexType_ShouldSetReference()
    {
        // Arrange
        var list = new CoreList<int> { 1, 2, 3 };
        var newList = new CoreList<int> { 4, 5, 6 };
        _root.Items = list;
        var op = CreateOp<CoreList<int>?>(_root, "Items", newList, list);

        // Act
        op.Apply(_context);

        // Assert
        Assert.That(_root.Items, Is.SameAs(newList));
    }

    [Test]
    public void Revert_WithComplexType_ShouldRestoreReference()
    {
        // Arrange
        var oldList = new CoreList<int> { 1, 2, 3 };
        var newList = new CoreList<int> { 4, 5, 6 };
        _root.Items = newList;
        var op = CreateOp<CoreList<int>?>(_root, "Items", newList, oldList);

        // Act
        op.Revert(_context);

        // Assert
        Assert.That(_root.Items, Is.SameAs(oldList));
    }

    [Test]
    public void Apply_WithNonAnimatableProperty_AnimationPath_ShouldNotThrow()
    {
        // Arrange - SimpleProperty (non-animatable) doesn't support Animation
        var engineObj = new TestEngineObject();
        var context = new OperationExecutionContext(engineObj);
        var op = CreateOp<IAnimation?>(engineObj, "FloatValue.Animation", null, null);

        // Act & Assert - Should not throw, but also shouldn't do anything
        Assert.DoesNotThrow(() => op.Apply(context));
    }

    #endregion

    #region Test Helper Classes

    private class TestCoreObject : CoreObject
    {
        public static readonly CoreProperty<int> ValueProperty;
        public static readonly CoreProperty<string?> TitleProperty;
        public static readonly CoreProperty<CoreList<int>?> ItemsProperty;

        private int _value;
        private string? _title;
        private CoreList<int>? _items;

        static TestCoreObject()
        {
            ValueProperty = ConfigureProperty<int, TestCoreObject>(nameof(Value))
                .Accessor(o => o.Value, (o, v) => o.Value = v)
                .Register();

            TitleProperty = ConfigureProperty<string?, TestCoreObject>(nameof(Title))
                .Accessor(o => o.Title, (o, v) => o.Title = v)
                .Register();

            ItemsProperty = ConfigureProperty<CoreList<int>?, TestCoreObject>(nameof(Items))
                .Accessor(o => o.Items, (o, v) => o.Items = v)
                .Register();
        }

        public int Value
        {
            get => _value;
            set => SetAndRaise(ValueProperty, ref _value, value);
        }

        public string? Title
        {
            get => _title;
            set => SetAndRaise(TitleProperty, ref _title, value);
        }

        public CoreList<int>? Items
        {
            get => _items;
            set => SetAndRaise(ItemsProperty, ref _items, value);
        }
    }

    [SuppressResourceClassGeneration]
    private class TestEngineObject : EngineObject
    {
        public TestEngineObject()
        {
            FloatValue = Property.Create(0f);
            ScanProperties<TestEngineObject>();
        }

        public IProperty<float> FloatValue { get; }
    }

    [SuppressResourceClassGeneration]
    private class TestAnimatableEngineObject : EngineObject
    {
        public TestAnimatableEngineObject()
        {
            FloatValue = Property.CreateAnimatable(0f);
            ScanProperties<TestAnimatableEngineObject>();
        }

        public IProperty<float> FloatValue { get; }
    }

    private class TestChangeOperation : ChangeOperation
    {
        public override void Apply(OperationExecutionContext context) { }
        public override void Revert(OperationExecutionContext context) { }
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
