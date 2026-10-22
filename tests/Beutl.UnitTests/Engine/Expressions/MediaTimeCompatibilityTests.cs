using System.Globalization;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Graphics.Transitions;

namespace Beutl.UnitTests.Engine.Expressions;

// What projects saved before #2885 hold for the time properties, now MediaTime, keeps working.
[TestFixture]
public sealed class MediaTimeCompatibilityTests
{
    [Test]
    public void A_saved_expression_that_returns_a_TimeSpan_sets_a_time()
    {
        Assert.That(Expression.TryParse<MediaTime>("TimeSpan.FromSeconds(1.5)", out var expression, out string? error), Is.True, error);
        Assert.That(expression!.Evaluate(TestHelper.CreateExpressionContext(MediaTime.Zero)), Is.EqualTo(MediaTime.FromSeconds(1.5)));
    }

    [Test]
    public void A_saved_expression_reads_a_time_property_as_a_TimeSpan()
    {
        var root = new TestEngineObject();
        var controller = new DrawableTimeController { OffsetPosition = { Value = MediaTime.FromSeconds(2.5) } };
        root.AddChild(controller);
        var context = new ExpressionContext(MediaTime.Zero, Property.CreateExpression(0.0), new PropertyLookup(root));
        var globals = new ExpressionGlobals(context);

        Assert.Multiple(() =>
        {
            Assert.That(globals.GetProperty<TimeSpan>(controller.Id, "OffsetPosition"), Is.EqualTo(TimeSpan.FromSeconds(2.5)));
            Assert.That(globals.GetProperty<MediaTime>(controller.Id, "OffsetPosition"), Is.EqualTo(MediaTime.FromSeconds(2.5)));
        });
    }

    // The limits of [Range] stay TimeSpan text and are compared with the time as a TimeSpan.
    [TestCase("00:00:00", true)]
    [TestCase("01:00:00", true)]
    [TestCase("01:00:00.0000001", false)]
    [TestCase("-00:00:00.0000001", false)]
    public void A_transition_keeps_its_duration_within_its_range(string text, bool accepted)
    {
        var transition = new CrossDissolveTransition();
        MediaTime duration = MediaTime.FromTimeSpan(TimeSpan.Parse(text, CultureInfo.InvariantCulture));

        transition.Duration.Value = duration;

        Assert.That(transition.Duration.Value == duration, Is.EqualTo(accepted));
    }
}
