using System.ComponentModel.DataAnnotations;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Validation;
using BeutlValidationContext = Beutl.Validation.ValidationContext;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

public class ValidationEvaluatorTests
{
    [Test]
    public void EvaluateCoreProperty_ReportsCoercionAndRejection()
    {
        var target = new RangedCoreObject();

        ValidationOutcome coerced = ValidationEvaluator.Evaluate(target, RangedCoreObject.AmountProperty, 50, options: null);
        ValidationOutcome rejected = ValidationEvaluator.Evaluate(target, RangedCoreObject.AmountProperty, "bad", options: null);

        Assert.Multiple(() =>
        {
            Assert.That(coerced.Status, Is.EqualTo(ValidationStatus.Coerced));
            Assert.That(coerced.CoercedValue!.GetValue<int>(), Is.EqualTo(10));
            Assert.That(coerced.OriginalValue!.GetValue<int>(), Is.EqualTo(50));
            Assert.That(rejected.Status, Is.EqualTo(ValidationStatus.Rejected));
        });
    }

    [Test]
    public void EvaluateEngineProperty_ReportsCoercionAndRejection()
    {
        var target = new RangedEngineObject();

        ValidationOutcome coerced = ValidationEvaluator.Evaluate(target.Amount, 50, options: null);
        ValidationOutcome rejected = ValidationEvaluator.Evaluate(target.Amount, "bad", options: null);

        Assert.Multiple(() =>
        {
            Assert.That(coerced.Status, Is.EqualTo(ValidationStatus.Coerced));
            Assert.That(coerced.CoercedValue!.GetValue<int>(), Is.EqualTo(10));
            Assert.That(rejected.Status, Is.EqualTo(ValidationStatus.Rejected));
        });
    }

    [Test]
    public void Color_and_pen_rejections_include_agent_action_hints()
    {
        var target = new TypedEngineObject();

        ValidationOutcome color = ValidationEvaluator.Evaluate(target.Color, "Amber", options: null);
        ValidationOutcome pen = ValidationEvaluator.Evaluate(target.Pen, new object(), options: null);

        Assert.Multiple(() =>
        {
            Assert.That(color.Status, Is.EqualTo(ValidationStatus.Rejected));
            Assert.That(color.Hint, Does.Contain("#ffffb34d"));
            Assert.That(color.Hint, Does.Contain("Amber"));
            Assert.That(pen.Status, Is.EqualTo(ValidationStatus.Rejected));
            Assert.That(pen.Hint, Does.Contain("get_schema"));
            Assert.That(pen.Hint, Does.Contain("Pen"));
        });
    }

    [Test]
    public void EvaluateAnimationValue_UsesTheAttachedValidatorInsteadOfRebuildingAttributes()
    {
        IProperty<int> property = Property.CreateAnimatable(0, new RejectingValidator());

        ValidationOutcome outcome = ValidationEvaluator.EvaluateAnimationValue(
            property,
            42,
            options: null);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(ValidationStatus.Rejected));
            Assert.That(outcome.Message, Is.EqualTo("custom animation rejection"));
        });
    }

    [Test]
    public void EvaluateEngineProperty_RunsCustomValidatorBeforeMissingFontWarning()
    {
        var validator = new RejectingFontValidator();
        IProperty<FontFamily> property = Property.Create(new FontFamily("default"), validator);

        ValidationOutcome outcome = ValidationEvaluator.Evaluate(
            property,
            new FontFamily($"missing-{Guid.NewGuid():N}"),
            options: null);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(ValidationStatus.Rejected));
            Assert.That(outcome.Message, Is.EqualTo("custom font rejection"));
        });
    }

    private sealed class RangedCoreObject : CoreObject
    {
        public static readonly CoreProperty<int> AmountProperty =
            ConfigureProperty<int, RangedCoreObject>(nameof(Amount))
                .DefaultValue(0)
                .Register();

        [System.ComponentModel.DataAnnotations.Range(0, 10)]
        public int Amount
        {
            get => GetValue(AmountProperty);
            set => SetValue(AmountProperty, value);
        }
    }

    private sealed class RangedEngineObject : EngineObject
    {
        public RangedEngineObject()
        {
            ScanProperties<RangedEngineObject>();
        }

        [System.ComponentModel.DataAnnotations.Range(0, 10)]
        public IProperty<int> Amount { get; } = Property.Create(0);
    }

    private sealed class TypedEngineObject : EngineObject
    {
        public TypedEngineObject()
        {
            ScanProperties<TypedEngineObject>();
        }

        public IProperty<Color> Color { get; } = Property.Create(Colors.White);

        public IProperty<Pen?> Pen { get; } = Property.Create<Pen?>();
    }

    private sealed class RejectingValidator : IValidator<int>
    {
        public bool TryCoerce(BeutlValidationContext context, ref int value) => false;

        public string? Validate(BeutlValidationContext context, int value) => "custom animation rejection";
    }

    private sealed class RejectingFontValidator : IValidator<FontFamily>
    {
        public bool TryCoerce(BeutlValidationContext context, ref FontFamily? value) => false;

        public string? Validate(BeutlValidationContext context, FontFamily? value) => "custom font rejection";
    }
}
