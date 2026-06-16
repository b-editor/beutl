using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// Regression for the StrokeEffect.Offset "object sticks to the top-left" bug. Source pixels stay put (offset
// only moves the stroke), but the OUTPUT BOUNDS used to grow only toward the offset (rect.Union(border.Translate(offset))).
// The editor centers the selection box / transform-handle overlay on those bounds
// (TransformHandleMath.AlignUserMatrixToRenderedBounds), so an asymmetric box pinned the source to the opposite corner.
// The output box must stay CENTERED on the source for any offset; offset == 0 is unchanged.
[TestFixture]
public class StrokeEffectOffsetBoundsTests
{
    private static readonly Rect Source = new(0, 0, 100, 100); // center (50, 50)

    private static Rect TransformedBounds(Point offset, float penOffset = 0f)
    {
        var pen = new Pen();
        pen.Thickness.CurrentValue = 14;
        pen.Brush.CurrentValue = Brushes.Red;
        pen.Offset.CurrentValue = penOffset;
        var effect = new StrokeEffect();
        effect.Pen.CurrentValue = pen;
        effect.Offset.CurrentValue = offset;

        var resource = effect.ToResource(CompositionContext.Default);
        var context = new FilterEffectContext(Source);
        // CustomEffect updates context.Bounds via TransformBounds — no GPU needed for the bounds pass.
        resource.GetOriginal().ApplyTo(context, resource);
        return context.Bounds;
    }

    [Test]
    public void ZeroOffset_BoundsCenteredOnSource()
    {
        Rect bounds = TransformedBounds(default);
        Assert.That(bounds.Center.X, Is.EqualTo(Source.Center.X).Within(0.001));
        Assert.That(bounds.Center.Y, Is.EqualTo(Source.Center.Y).Within(0.001));
    }

    [TestCase(50f, 30f)]
    [TestCase(-50f, -30f)]
    [TestCase(80f, -20f)]
    [TestCase(0f, 60f)]
    public void NonZeroOffset_KeepsBoxCenteredOnSource(float ox, float oy)
    {
        Rect bounds = TransformedBounds(new Point(ox, oy));

        // Centered on the source, NOT drifting by offset/2 (the old asymmetric union).
        Assert.That(bounds.Center.X, Is.EqualTo(Source.Center.X).Within(0.001),
            "output box drifted horizontally — source would appear pinned to a corner of its selection box");
        Assert.That(bounds.Center.Y, Is.EqualTo(Source.Center.Y).Within(0.001),
            "output box drifted vertically — source would appear pinned to a corner of its selection box");

        // It must still ENCLOSE the offset stroke (the box has to grow as the offset grows).
        Assert.That(bounds.Width, Is.GreaterThanOrEqualTo(Source.Width + 2 * MathF.Abs(ox)));
        Assert.That(bounds.Height, Is.GreaterThanOrEqualTo(Source.Height + 2 * MathF.Abs(oy)));
    }

    // PenHelper.GetBounds inflates by pen.Offset too, so the box must STILL be centered when the pen has its own Offset.
    [TestCase(0f)]
    [TestCase(10f)]
    public void PenOffset_BoxStaysCenteredOnSource(float penOffset)
    {
        Rect bounds = TransformedBounds(default, penOffset);
        Assert.That(bounds.Center.X, Is.EqualTo(Source.Center.X).Within(0.001));
        Assert.That(bounds.Center.Y, Is.EqualTo(Source.Center.Y).Within(0.001));
    }
}
