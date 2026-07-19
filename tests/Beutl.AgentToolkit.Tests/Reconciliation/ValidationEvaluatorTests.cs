using System.ComponentModel.DataAnnotations;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

public class ValidationEvaluatorTests
{
    [Test]
    public void EvaluateCoreProperty_ReportsCoercionAndRejection()
    {
        var target = new RangedCoreObject();

        ValidationOutcome coerced = ValidationEvaluator.Evaluate(target, RangedCoreObject.AmountProperty, 50);
        ValidationOutcome rejected = ValidationEvaluator.Evaluate(target, RangedCoreObject.AmountProperty, "bad");

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

        ValidationOutcome coerced = ValidationEvaluator.Evaluate(target.Amount, 50);
        ValidationOutcome rejected = ValidationEvaluator.Evaluate(target.Amount, "bad");

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

        ValidationOutcome color = ValidationEvaluator.Evaluate(target.Color, "Amber");
        ValidationOutcome pen = ValidationEvaluator.Evaluate(target.Pen, new object());

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
}
