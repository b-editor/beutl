using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Guards the render half of the StrokeEffect.Offset fix (commit 0019b728a): the Apply() origin rewrite that
// keeps the source anchored at its bounds while only the stroke moves by Offset. StrokeEffectOffsetBoundsTests
// does not exercise Apply(), leaving the source-placement origin otherwise untested. White fill = source,
// red pen = stroke; the two are measured separately by colour.
[NonParallelizable]
[TestFixture]
public class StrokeEffectRenderTests
{
    private static readonly PixelSize Frame = new(220, 220);
    private const float Center = 110f; // 220 / 2

    private static Drawable.Resource Make(Point offset, float penOffset, StrokeEffect.StrokeStyles style)
    {
        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.Width.CurrentValue = 80;
        shape.Height.CurrentValue = 80;
        shape.Fill.CurrentValue = Brushes.White;
        var pen = new Pen();
        pen.Thickness.CurrentValue = 12;
        pen.Brush.CurrentValue = Brushes.Red;
        pen.Offset.CurrentValue = penOffset;
        var e = new StrokeEffect();
        e.Pen.CurrentValue = pen;
        e.Offset.CurrentValue = offset;
        e.Style.CurrentValue = style;
        shape.FilterEffect.CurrentValue = e;
        return shape.ToResource(CompositionContext.Default);
    }

    // Separate centroids for the white source and the red stroke.
    private static (double wx, double wy, int wn, double rx, double ry, int rn) Centroids(Bitmap bmp)
    {
        double wx = 0, wy = 0, rx = 0, ry = 0;
        int wn = 0, rn = 0;
        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                var p = bmp.SKBitmap.GetPixel(x, y);
                if (p.Red > 150 && p.Green > 150 && p.Blue > 150) { wn++; wx += x; wy += y; }
                else if (p.Red > 150 && p.Green < 90 && p.Blue < 90) { rn++; rx += x; ry += y; }
            }
        }

        return (wn == 0 ? 0 : wx / wn, wn == 0 ? 0 : wy / wn, wn,
                rn == 0 ? 0 : rx / rn, rn == 0 ? 0 : ry / rn, rn);
    }

    // The source (white) must stay anchored on the frame centre for every Offset / pen.Offset / style, while the
    // stroke (red) moves by Offset. For a plain pen the old code already centred the source (its origin compensated
    // the asymmetric box), so the positive/negative/Foreground cases pass on pre-fix code too — they document the
    // contract and guard future breakage; the box-centering regression itself is covered by
    // StrokeEffectOffsetBoundsTests. Only Stroke_PenOffset distinguishes old from new: the old origin used
    // min(Offset, thickness) and ignored pen.Offset, so the source drifted by pen.Offset; the fix anchors it.
    [TestCase(0f, 0f, 0f, StrokeEffect.StrokeStyles.Background, TestName = "Stroke_NoOffset")]
    [TestCase(50f, 50f, 0f, StrokeEffect.StrokeStyles.Background, TestName = "Stroke_PositiveOffset")]
    [TestCase(-50f, -50f, 0f, StrokeEffect.StrokeStyles.Background, TestName = "Stroke_NegativeOffset")]
    [TestCase(0f, 0f, 10f, StrokeEffect.StrokeStyles.Background, TestName = "Stroke_PenOffset_NoStrokeOffset")]
    [TestCase(40f, 40f, 0f, StrokeEffect.StrokeStyles.Foreground, TestName = "Stroke_Foreground")]
    public void Source_StaysCentered_StrokeMovesByOffset(float ox, float oy, float penOffset, StrokeEffect.StrokeStyles style)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap bmp = GoldenImageHarness.RenderAtScale(Make(new Point(ox, oy), penOffset, style), Frame, 1f);
            (double wx, double wy, int wn, double rx, double ry, int rn) = Centroids(bmp);

            Assert.That(wn, Is.GreaterThan(1000), "no white source drawn");
            // (1) the source is anchored on the frame centre regardless of Offset / pen.Offset / style.
            Assert.That(wx, Is.EqualTo(Center).Within(3.0), $"source drifted horizontally (wx={wx})");
            Assert.That(wy, Is.EqualTo(Center).Within(3.0), $"source drifted vertically (wy={wy})");

            // (2) the stroke moves by Offset sign-correctly: a non-zero offset leads the red centroid in its direction.
            if (ox > 0) Assert.That(rx, Is.GreaterThan(wx + 5), "stroke did not move right with +offset");
            if (ox < 0) Assert.That(rx, Is.LessThan(wx - 5), "stroke did not move left with -offset");
        });
    }
}
