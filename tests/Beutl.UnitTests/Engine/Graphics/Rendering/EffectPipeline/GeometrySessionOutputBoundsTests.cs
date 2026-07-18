using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Seam-level cover for <see cref="GeometrySession.SetOutputBounds"/> (feature 004, §C3): the render-time output
/// tightening hatch. A shrink fully contained in the pass's allocated bounds is honored; a grow past the allocated
/// bounds throws (the buffer cannot supply pixels it never held); the last call wins; and <see cref="GeometrySession.DiscardOutput"/>
/// supersedes a requested shrink regardless of call order.
/// </summary>
[NonParallelizable]
[TestFixture]
public class GeometrySessionOutputBoundsTests
{
    private static readonly Rect s_bounds = new(0, 0, 100, 100);

    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
    }

    [Test]
    public void SetOutputBounds_ShrinkWithinBounds_IsHonored()
    {
        WithSession(session =>
        {
            var tight = new Rect(20, 20, 40, 40);
            session.SetOutputBounds(tight);

            Assert.That(session.ShrunkOutputBounds, Is.EqualTo(tight));
            Assert.That(session.IsOutputDiscarded, Is.False);
        });
    }

    [Test]
    public void SetOutputBounds_EdgeTouchingShrink_IsHonored()
    {
        WithSession(session =>
        {
            // Touching every allocated edge is still a valid shrink (contained, inclusive of the bounds edges).
            session.SetOutputBounds(s_bounds);
            Assert.That(session.ShrunkOutputBounds, Is.EqualTo(s_bounds));
        });
    }

    [Test]
    public void SetOutputBounds_GrowPastBounds_Throws()
    {
        WithSession(session =>
        {
            Assert.Throws<ArgumentException>(() => session.SetOutputBounds(new Rect(-10, 0, 100, 100)),
                "a request extending outside the allocated bounds (a grow) must throw");
            Assert.Throws<ArgumentException>(() => session.SetOutputBounds(new Rect(0, 0, 120, 100)),
                "a request wider than the allocated bounds (a grow) must throw");
            Assert.That(session.ShrunkOutputBounds, Is.Null, "a rejected request must not mutate the session");
        });
    }

    [Test]
    public void SetOutputBounds_LastCallWins()
    {
        WithSession(session =>
        {
            session.SetOutputBounds(new Rect(10, 10, 30, 30));
            var last = new Rect(40, 40, 20, 20);
            session.SetOutputBounds(last);

            Assert.That(session.ShrunkOutputBounds, Is.EqualTo(last));
        });
    }

    [Test]
    public void DiscardOutput_SupersedesShrink_RegardlessOfOrder()
    {
        WithSession(session =>
        {
            session.SetOutputBounds(new Rect(20, 20, 40, 40));
            session.DiscardOutput();
            Assert.That(session.IsOutputDiscarded, Is.True,
                "discard set after a shrink still drops the output (the executor checks discard first)");
        });

        WithSession(session =>
        {
            session.DiscardOutput();
            session.SetOutputBounds(new Rect(20, 20, 40, 40));
            Assert.That(session.IsOutputDiscarded, Is.True,
                "a shrink after discard does not clear the discard (discard supersedes regardless of order)");
        });
    }

    private static void WithSession(Action<GeometrySession> body)
    {
        var size = PixelRect.FromRect(s_bounds);
        using RenderTarget target = RenderTarget.Create(size.Width, size.Height)
            ?? throw new InvalidOperationException("RenderTarget.Create returned null (raster surface unavailable).");
        using var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, 1f, logicalSize: s_bounds.Size);
        var session = new GeometrySession(
            canvas, [], s_bounds, outputScale: 1f, workingScale: 1f, maxWorkingScale: float.PositiveInfinity);
        body(session);
    }
}
