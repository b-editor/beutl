using System.ComponentModel.DataAnnotations;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.Engine;

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
            Assert.That(coerced.CoercedValue, Is.EqualTo(10));
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
            Assert.That(coerced.CoercedValue, Is.EqualTo(10));
            Assert.That(rejected.Status, Is.EqualTo(ValidationStatus.Rejected));
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
}
