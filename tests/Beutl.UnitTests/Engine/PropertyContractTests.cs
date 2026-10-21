using Beutl.Composition;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Validation;

namespace Beutl.UnitTests.Engine;

// The contract SimpleProperty and AnimatableProperty share. What only one of them does stays in its own tests.
[TestFixture(PropertyContractTests.PropertyKind.Simple)]
[TestFixture(PropertyContractTests.PropertyKind.Animatable)]
public class PropertyContractTests(PropertyContractTests.PropertyKind kind)
{
    public enum PropertyKind
    {
        Simple,
        Animatable,
    }

    private IExpressionProperty<T> Create<T>(T defaultValue, IValidator<T>? validator = null)
        => kind == PropertyKind.Simple
            ? new SimpleProperty<T>(defaultValue, validator)
            : new AnimatableProperty<T>(defaultValue, validator);

    private IExpressionProperty<T> Make<T>(T defaultValue)
    {
        IExpressionProperty<T> property = Create(defaultValue);
        property.SetAttributes("Value", []);
        return property;
    }

    private static void ResetToDefault<T>(IExpressionProperty<T> property)
    {
        switch (property)
        {
            case SimpleProperty<T> simple:
                simple.ResetToDefault();
                break;
            case AnimatableProperty<T> animatable:
                animatable.ResetToDefault();
                break;
        }
    }

    [Test]
    public void Name_BeforeInitialization_Throws()
    {
        IExpressionProperty<int> property = Create(0);

        Assert.Throws<InvalidOperationException>(() => _ = property.Name);
    }

    [Test]
    public void CurrentValue_NewValue_RaisesValueChangedAndEdited()
    {
        IExpressionProperty<int> property = Make(0);
        PropertyValueChangedEventArgs<int>? args = null;
        int edited = 0;
        property.ValueChanged += (_, e) => args = e;
        property.Edited += (_, _) => edited++;

        property.Value = 42;

        Assert.That(args, Is.Not.Null);
        Assert.That(args!.Property, Is.SameAs(property));
        Assert.That(args.OldValue, Is.EqualTo(0));
        Assert.That(args.NewValue, Is.EqualTo(42));
        Assert.That(edited, Is.EqualTo(1));
        Assert.That(property.HasLocalValue, Is.True);
    }

    [Test]
    public void CurrentValue_SameValue_DoesNotRaiseEvents()
    {
        IExpressionProperty<int> property = Make(10);
        int valueChanged = 0;
        int edited = 0;
        property.ValueChanged += (_, _) => valueChanged++;
        property.Edited += (_, _) => edited++;

        property.Value = 10;

        Assert.That(valueChanged, Is.Zero);
        Assert.That(edited, Is.Zero);
        Assert.That(property.HasLocalValue, Is.False);
    }

    [Test]
    public void SetValueEvenIfEqual_EquivalentInstance_ReplacesAndNotifiesOnce()
    {
        var current = new EqualityValue("same", "old");
        var replacement = new EqualityValue("same", "new");
        var validator = new CountingValidator();
        IExpressionProperty<EqualityValue> property = Create(current, validator);
        property.SetAttributes("Value", []);
        PropertyValueChangedEventArgs<EqualityValue>? args = null;
        int edited = 0;
        property.ValueChanged += (_, e) => args = e;
        property.Edited += (_, _) => edited++;

        ((IProperty)property).SetValueEvenIfEqual(replacement);

        Assert.Multiple(() =>
        {
            Assert.That(property.Value, Is.SameAs(replacement));
            Assert.That(property.Value.State, Is.EqualTo("new"));
            Assert.That(validator.CoerceCount, Is.EqualTo(1));
            Assert.That(args!.OldValue, Is.SameAs(current));
            Assert.That(args.NewValue, Is.SameAs(replacement));
            Assert.That(edited, Is.EqualTo(1));
            Assert.That(property.HasLocalValue, Is.True);
        });
    }

    [Test]
    public void Expression_AssigningRaisesExpressionChangedAndEdited()
    {
        IExpressionProperty<double> property = Make(0.0);
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
    public void GetOwnerObject_AfterSet_ReturnsAssignedOwner()
    {
        IExpressionProperty<int> property = Make(0);
        var owner = new TestEngineObjectForProperty();

        property.SetOwnerObject(owner);

        Assert.That(property.GetOwnerObject(), Is.SameAs(owner));
    }

    [Test]
    public void GetValidator_ReturnsTheAttachedValidator()
    {
        var validator = new MultipleValidator<int>([]);
        IExpressionProperty<int> property = Create(0, validator);

        Assert.That(property.GetValidator(), Is.SameAs(validator));
    }

    [Test]
    public void GetValue_WithoutAnimationOrExpression_ReturnsCurrentValue()
    {
        IExpressionProperty<int> property = Make(0);
        property.Value = 99;

        int value = property.Evaluate(CompositionContext.Default);

        Assert.That(value, Is.EqualTo(99));
    }

    [Test]
    public void ResetToDefault_RestoresDefaultsAndClearsState()
    {
        IExpressionProperty<int> property = Make(7);
        property.Value = 100;
        property.Expression = Expression.Create<int>("1+2");

        ResetToDefault(property);

        Assert.That(property.Value, Is.EqualTo(7));
        Assert.That(property.HasLocalValue, Is.False);
        Assert.That(property.HasExpression, Is.False);
    }
}

// Equal by Key alone, so a replacement can be equivalent to the current value and still be another instance.
internal sealed class EqualityValue(string key, string state)
{
    public string Key { get; } = key;

    public string State { get; } = state;

    public override bool Equals(object? obj)
    {
        return obj is EqualityValue other && Key == other.Key;
    }

    public override int GetHashCode()
    {
        return Key.GetHashCode(StringComparison.Ordinal);
    }
}

internal sealed class CountingValidator : IValidator<EqualityValue>
{
    public int CoerceCount { get; private set; }

    public bool TryCoerce(ValidationContext context, ref EqualityValue? value)
    {
        CoerceCount++;
        return true;
    }

    public string? Validate(ValidationContext context, EqualityValue? value)
    {
        return null;
    }
}
