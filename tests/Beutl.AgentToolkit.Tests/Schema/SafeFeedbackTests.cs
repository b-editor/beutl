using Beutl.AgentToolkit.Reconciliation;
using Beutl.Graphics.Shapes;

namespace Beutl.AgentToolkit.Tests.Schema;

public sealed class SafeFeedbackTests
{
    [Test]
    public void Out_of_range_values_are_reported_before_apply()
    {
        var text = new TextBlock();
        ValidationOutcome outcome = ValidationEvaluator.Evaluate(text.Size, -10f, options: null);

        Assert.That(outcome.Status, Is.AnyOf(ValidationStatus.Coerced, ValidationStatus.Rejected));
    }
}
