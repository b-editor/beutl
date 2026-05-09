using Beutl.Composition;
using Beutl.Engine;
using Beutl.Engine.Expressions;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class SimplePropertyTests
{
    private static SimpleProperty<T> Make<T>(T defaultValue, string name = "Value")
    {
        var property = new SimpleProperty<T>(defaultValue);
        property.SetAttributes(name, []);
        return property;
    }

    [Test]
    public void Name_BeforeInitialization_Throws()
    {
        var property = new SimpleProperty<int>(0);

        Assert.Throws<InvalidOperationException>(() => _ = property.Name);
    }

    [Test]
    public void ValueType_ReportsGenericArgument()
    {
        var property = Make(0);

        Assert.That(property.ValueType, Is.EqualTo(typeof(int)));
    }

    [Test]
    public void CurrentValue_SetSameValue_DoesNotRaiseEvents()
    {
        var property = Make(10);
        int valueChanged = 0;
        int edited = 0;

        property.ValueChanged += (_, _) => valueChanged++;
        property.Edited += (_, _) => edited++;

        property.CurrentValue = 10;

        Assert.That(valueChanged, Is.Zero);
        Assert.That(edited, Is.Zero);
        Assert.That(property.HasLocalValue, Is.False);
    }

    [Test]
    public void CurrentValue_NewValue_RaisesValueChangedAndEdited()
    {
        var property = Make(0);
        PropertyValueChangedEventArgs<int>? args = null;
        int edited = 0;
        property.ValueChanged += (_, e) => args = e;
        property.Edited += (_, _) => edited++;

        property.CurrentValue = 42;

        Assert.That(args, Is.Not.Null);
        Assert.That(args!.Property, Is.SameAs(property));
        Assert.That(args.OldValue, Is.EqualTo(0));
        Assert.That(args.NewValue, Is.EqualTo(42));
        Assert.That(edited, Is.EqualTo(1));
        Assert.That(property.HasLocalValue, Is.True);
    }

    [Test]
    public void CompoundAssign_SetsCurrentValue()
    {
        var property = Make(0);

        property <<= 7;

        Assert.That(property.CurrentValue, Is.EqualTo(7));
    }

    [Test]
    public void Animation_SetNonNull_Throws()
    {
        var property = Make(0);

        Assert.Throws<InvalidOperationException>(() => property.Animation = new Beutl.Animation.KeyFrameAnimation<int>());
    }

    [Test]
    public void Animation_SetNull_DoesNotThrow()
    {
        var property = Make(0);

        Assert.DoesNotThrow(() => property.Animation = null);
        Assert.That(property.Animation, Is.Null);
    }

    [Test]
    public void Expression_AssigningRaisesExpressionChangedAndEdited()
    {
        var property = Make(0.0);
        IExpression<double>? captured = null;
        int edited = 0;
        property.ExpressionChanged += e => captured = e;
        property.Edited += (_, _) => edited++;

        var expression = Expression.Create<double>("1+2");
        property.Expression = expression;

        Assert.That(captured, Is.SameAs(expression));
        Assert.That(edited, Is.EqualTo(1));
        Assert.That(property.HasExpression, Is.True);
    }

    [Test]
    public void Expression_AssigningSameValue_DoesNotRaise()
    {
        var property = Make(0.0);
        var expression = Expression.Create<double>("1+2");
        property.Expression = expression;
        int edited = 0;
        property.Edited += (_, _) => edited++;

        property.Expression = expression;

        Assert.That(edited, Is.Zero);
    }

    [Test]
    public void GetValue_WithoutExpression_ReturnsCurrentValue()
    {
        var property = Make(0);
        property.CurrentValue = 99;

        int value = property.GetValue(CompositionContext.Default);

        Assert.That(value, Is.EqualTo(99));
    }

    [Test]
    public void ResetToDefault_RestoresDefaultsAndClearsState()
    {
        var property = Make(7);
        property.CurrentValue = 100;
        property.Expression = Expression.Create<int>("1+2");

        property.ResetToDefault();

        Assert.That(property.CurrentValue, Is.EqualTo(7));
        Assert.That(property.HasLocalValue, Is.False);
        Assert.That(property.HasExpression, Is.False);
    }

    [Test]
    public void GetAttributes_ReturnsAttributesPassedToSetAttributes()
    {
        var attrs = new Attribute[] { new ObsoleteAttribute("test") };
        var property = new SimpleProperty<int>(0);
        property.SetAttributes("Foo", attrs);

        Assert.That(property.GetAttributes(), Is.SameAs(attrs));
        Assert.That(property.Name, Is.EqualTo("Foo"));
    }

    [Test]
    public void GetOwnerObject_AfterSet_ReturnsAssignedOwner()
    {
        var property = Make(0);
        var owner = new TestEngineObjectForProperty();

        property.SetOwnerObject(owner);

        Assert.That(property.GetOwnerObject(), Is.SameAs(owner));
    }

    [Test]
    public void SetOwnerObject_SameValue_NoEffect()
    {
        var property = Make(0);
        var owner = new TestEngineObjectForProperty();
        property.SetOwnerObject(owner);

        // Should not throw or alter behavior.
        property.SetOwnerObject(owner);

        Assert.That(property.GetOwnerObject(), Is.SameAs(owner));
    }

    [Test]
    public void ToString_WithoutExpression_FormatsSimpleSummary()
    {
        var property = Make(5);

        Assert.That(property.ToString(), Does.Contain("Simple"));
        Assert.That(property.ToString(), Does.Contain("Default: 5"));
    }

    [Test]
    public void ToString_WithExpression_IncludesExpressionString()
    {
        var property = Make(0.0);
        property.Expression = Expression.Create<double>("1+2");

        Assert.That(property.ToString(), Does.Contain("Expression"));
        Assert.That(property.ToString(), Does.Contain("1+2"));
    }

    [Test]
    public void ToAnimatable_PreservesLocalValue()
    {
        var property = Make(1);
        property.CurrentValue = 42;

        IProperty<int> animatable = property.ToAnimatable();

        Assert.That(animatable, Is.InstanceOf<AnimatableProperty<int>>());
        Assert.That(animatable.DefaultValue, Is.EqualTo(1));
        Assert.That(animatable.CurrentValue, Is.EqualTo(42));
        Assert.That(animatable.IsAnimatable, Is.True);
    }

    [Test]
    public void ToAnimatable_WithoutLocalValue_KeepsDefault()
    {
        var property = Make(7);

        IProperty<int> animatable = property.ToAnimatable();

        Assert.That(animatable.CurrentValue, Is.EqualTo(7));
        Assert.That(animatable.HasLocalValue, Is.False);
    }
}

public partial class TestEngineObjectForProperty : EngineObject
{
    public TestEngineObjectForProperty()
    {
        ScanProperties<TestEngineObjectForProperty>();
    }
}
