using Avalonia;
using Beutl.Animation.Easings;
using Beutl.Editor.Components.TimelineTab.Views;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class TransitionRampTests
{
    private static readonly Size s_size = new(100, 20);

    [Test]
    public void ALinearRamp_IsAStraightLineFromStartToEnd()
    {
        Point[] curve = TransitionRamp.CreateCurve(s_size, 0.5, 1, new LinearEasing());

        Assert.That(curve, Is.EqualTo(new[] { new Point(0, 10), new Point(100, 0) }));
    }

    // The curve is the eased progress at each moment, so an ease-in stays low before rising.
    [TestCase(typeof(CubicEaseIn), 0.125)]
    [TestCase(typeof(CubicEaseOut), 0.875)]
    [TestCase(typeof(CubicEaseInOut), 0.5)]
    public void AnEasedRamp_FollowsItsCurve(Type easingType, double heightAtMiddle)
    {
        Point[] curve = TransitionRamp.CreateCurve(s_size, 0, 1, (Easing)Activator.CreateInstance(easingType)!);
        Point middle = curve.Single(point => point.X == 50);

        Assert.Multiple(() =>
        {
            Assert.That(curve[0], Is.EqualTo(new Point(0, 20)));
            Assert.That(curve[^1], Is.EqualTo(new Point(100, 0)));
            Assert.That(middle.Y, Is.EqualTo(20 * (1 - heightAtMiddle)).Within(1e-4));
            Assert.That(curve.Length, Is.GreaterThan(8), "a curve needs more than one segment");
        });
    }

    // A curve that overshoots, as Back and Elastic easings do, holds at the end it passes.
    [Test]
    public void AnOvershootingCurve_StaysWithinThePart()
    {
        Point[] curve = TransitionRamp.CreateCurve(s_size, 0, 1, new BackEaseIn());

        Assert.That(curve.Select(point => point.Y), Is.All.InRange(0, 20));
    }

    // Each element draws its own share of the span; the two shares meet at the cut at the same height.
    [Test]
    public void ThePartsOnEitherSideOfTheCut_MeetAtTheSameHeight()
    {
        Point[] outgoing = TransitionRamp.CreateCurve(s_size, 0, 1d / 3, new CubicEaseIn());
        Point[] incoming = TransitionRamp.CreateCurve(s_size, 1d / 3, 1, new CubicEaseIn());

        Assert.That(outgoing[^1].Y, Is.EqualTo(incoming[0].Y).Within(1e-4));
    }
}
