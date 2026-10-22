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
    public void EvaluateValueOnlyProperty_ReportsCoercionAndRejection()
    {
        var target = new RangedCoreObject();

        ValidationOutcome coerced = ValidationEvaluator.Evaluate(target.AmountProperty, 50, options: null);
        ValidationOutcome rejected = ValidationEvaluator.Evaluate(target.AmountProperty, "bad", options: null);

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
        IExpressionProperty<int> property = Property.CreateAnimatable(0, new RejectingValidator());

        ValidationOutcome outcome = ValidationEvaluator.Evaluate(
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
        IExpressionProperty<FontFamily> property = Property.CreateExpression(new FontFamily("default"), validator);

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
        public RangedCoreObject()
        {
            ScanProperties<RangedCoreObject>();
        }

        [System.ComponentModel.DataAnnotations.Range(0, 10)]
        public IProperty<int> AmountProperty { get; } = new ValueOnlyProperty<int>(0);

        public int Amount
        {
            get => AmountProperty.Value;
            set => AmountProperty.Value = value;
        }
    }

    private sealed class RangedEngineObject : EngineObject
    {
        public RangedEngineObject()
        {
            ScanProperties<RangedEngineObject>();
        }

        [System.ComponentModel.DataAnnotations.Range(0, 10)]
        public IExpressionProperty<int> Amount { get; } = Property.CreateExpression(0);
    }

    private sealed class TypedEngineObject : EngineObject
    {
        public TypedEngineObject()
        {
            ScanProperties<TypedEngineObject>();
        }

        public IExpressionProperty<Color> Color { get; } = Property.CreateExpression(Colors.White);

        public IExpressionProperty<Pen?> Pen { get; } = Property.CreateExpression<Pen?>();
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
