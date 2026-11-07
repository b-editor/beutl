using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Guards the render half of the StrokeEffect.Offset fix: the Apply() origin rewrite that
// keeps the source anchored at its bounds while only the stroke moves by Offset. StrokeEffectOffsetBoundsTests
// does not exercise Apply(), leaving the source-placement origin otherwise untested. It also holds the stroke to the
// shape's own pen, where the outline it follows lies. White fill = source, red pen = stroke; the two are measured
// separately by colour.
[NonParallelizable]
[TestFixture]
public class StrokeEffectRenderTests
{
    private static readonly PixelSize Frame = new(220, 220);
    private const float Center = 110f; // 220 / 2

    private static Drawable.Resource Make(Point offset, float penOffset, StrokeOrder order)
    {
        var shape = new RectShape();
        shape.AlignmentX.Value = AlignmentX.Center;
        shape.AlignmentY.Value = AlignmentY.Center;
        shape.Width.Value = 80;
        shape.Height.Value = 80;
        shape.Fill.Value = Brushes.White;
        var pen = new Pen();
        pen.Thickness.Value = 12;
        pen.Brush.Value = Brushes.Red;
        pen.Offset.Value = penOffset;
        pen.StrokeOrder.Value = order;
        var e = new StrokeEffect();
        e.Pen.Value = pen;
        e.Offset.Value = offset;
        shape.FilterEffect.Value = e;
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

    // Only Stroke_PenOffset fails on the old origin rule, which ignored pen.Offset; the other cases pin the contract.
    [TestCase(0f, 0f, 0f, StrokeOrder.AboveFill, TestName = "Stroke_NoOffset")]
    [TestCase(50f, 50f, 0f, StrokeOrder.AboveFill, TestName = "Stroke_PositiveOffset")]
    [TestCase(-50f, -50f, 0f, StrokeOrder.AboveFill, TestName = "Stroke_NegativeOffset")]
    [TestCase(0f, 0f, 10f, StrokeOrder.AboveFill, TestName = "Stroke_PenOffset_NoStrokeOffset")]
    [TestCase(40f, 40f, 0f, StrokeOrder.BelowFill, TestName = "Stroke_BelowContent")]
    public void Source_StaysCentered_StrokeMovesByOffset(float ox, float oy, float penOffset, StrokeOrder order)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap bmp = GoldenImageHarness.RenderAtScale(Make(new Point(ox, oy), penOffset, order), Frame, 1f);
            (double wx, double wy, int wn, double rx, double ry, int rn) = Centroids(bmp);

            Assert.That(wn, Is.GreaterThan(1000), "no white source drawn");
            // (1) the source is anchored on the frame centre regardless of Offset / pen.Offset / stroke order.
            Assert.That(wx, Is.EqualTo(Center).Within(3.0), $"source drifted horizontally (wx={wx})");
            Assert.That(wy, Is.EqualTo(Center).Within(3.0), $"source drifted vertically (wy={wy})");

            // (2) the stroke moves by Offset sign-correctly: a non-zero offset leads the red centroid in its direction.
            if (ox > 0) Assert.That(rx, Is.GreaterThan(wx + 5), "stroke did not move right with +offset");
            if (ox < 0) Assert.That(rx, Is.LessThan(wx - 5), "stroke did not move left with -offset");
        });
    }

    // The same white shape with a red pen, drawn by StrokeEffect around its alpha or by the shape itself.
    private static Drawable.Resource Outlined(Shape shape, float thickness, bool viaEffect)
    {
        shape.Fill.Value = Brushes.White;
        var pen = new Pen();
        pen.Thickness.Value = thickness;
        pen.Brush.Value = Brushes.Red;
        if (viaEffect)
        {
            var effect = new StrokeEffect();
            effect.Pen.Value = pen;
            shape.FilterEffect.Value = effect;
        }
        else
        {
            shape.Pen.Value = pen;
        }

        return shape.ToResource(CompositionContext.Default);
    }

    // How much of each pixel the red pen covers, in linear light: red less green is the pen whether it lies over the
    // black background or the white fill.
    private static float[] PenCoverage(Bitmap bitmap)
    {
        using Bitmap linear = bitmap.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb);
        ReadOnlySpan<byte> pixels = linear.GetPixelSpan();
        var coverage = new float[linear.Width * linear.Height];
        for (int i = 0; i < coverage.Length; i++)
            coverage[i] = (pixels[i * 4 + 2] - pixels[i * 4 + 1]) / 255f;

        return coverage;
    }

    private static EllipseShape Ellipse()
    {
        var shape = new EllipseShape();
        shape.Width.Value = 120;
        shape.Height.Value = 90;
        return shape;
    }

    // The outline used to be traced through whole pixels wherever alpha was above zero, so the stroke stepped around
    // a curve half a pixel outside it on average and a whole pixel in places. Traced at half coverage, it lies where
    // the ellipse's own pen draws, give or take the four-sample anti-aliasing both strokes are drawn with.
    [TestCase(2f)]
    [TestCase(8f)]
    public void Stroke_on_an_ellipse_lies_where_its_own_pen_draws(float thickness)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap viaEffect = GoldenImageHarness.RenderAtScale(Outlined(Ellipse(), thickness, true), Frame, 1f);
            using Bitmap viaPen = GoldenImageHarness.RenderAtScale(Outlined(Ellipse(), thickness, false), Frame, 1f);
            float[] effectCoverage = PenCoverage(viaEffect);
            float[] penCoverage = PenCoverage(viaPen);

            double difference = 0;
            for (int i = 0; i < effectCoverage.Length; i++)
                difference += Math.Abs(effectCoverage[i] - penCoverage[i]);

            // Spread over both edges of the ring, the difference is how far the edges lie apart on average.
            const double Perimeter = 331.4; // of a 120 x 90 ellipse
            double displacement = difference / (2 * Perimeter);
            Assert.That(displacement, Is.LessThan(0.2), $"the stroke's edges lie {displacement:F3} px from the pen's");
        });
    }

    // A mitered pen draws a 36 degree point of the star some ten pixels past it, and StrokeEffect draws it as far:
    // its outline puts back the point that half coverage cut, and its output allows for the miter (#4104). The top
    // point, which needs a little more than the output allows, loses under a pixel.
    [Test]
    public void Stroke_on_a_star_reaches_as_far_as_its_own_pen()
    {
        // GeometryShape centres the path's size in the frame from the path's own origin, so the star starts at it.
        const string Star = "M 57.063 0 L 71.17 40.584 L 114.126 41.459 L 79.888 67.416 L 92.33 108.541 "
                            + "L 57.063 84 L 21.796 108.541 L 34.238 67.416 L 0 41.459 L 42.956 40.584 Z";
        static Shape Make()
        {
            var shape = new GeometryShape();
            shape.Data.Value = PathGeometry.Parse(Star);
            return shape;
        }

        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap viaEffect = GoldenImageHarness.RenderAtScale(Outlined(Make(), 6, true), Frame, 1f);
            using Bitmap viaPen = GoldenImageHarness.RenderAtScale(Outlined(Make(), 6, false), Frame, 1f);

            (int Left, int Top, int Right, int Bottom) effectBounds = PenBounds(viaEffect);
            (int Left, int Top, int Right, int Bottom) penBounds = PenBounds(viaPen);
            Assert.Multiple(() =>
            {
                Assert.That(effectBounds.Left, Is.EqualTo(penBounds.Left).Within(1), "left point");
                Assert.That(effectBounds.Top, Is.EqualTo(penBounds.Top).Within(1), "top point");
                Assert.That(effectBounds.Right, Is.EqualTo(penBounds.Right).Within(1), "right point");
                Assert.That(effectBounds.Bottom, Is.EqualTo(penBounds.Bottom).Within(1), "bottom points");
            });
        });

        // The pixels the pen at least half covers.
        static (int Left, int Top, int Right, int Bottom) PenBounds(Bitmap bitmap)
        {
            float[] coverage = PenCoverage(bitmap);
            int left = bitmap.Width, top = bitmap.Height, right = -1, bottom = -1;
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    if (coverage[y * bitmap.Width + x] < 0.5f)
                        continue;

                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }

            return (left, top, right, bottom);
        }
    }

    // An upright rectangle on whole pixels, such as a picture's frame, traces to exactly its own sides and corners,
    // where the old outline lay a pixel inside its right and bottom sides.
    [Test]
    public void Stroke_on_an_upright_rectangle_matches_its_own_pen()
    {
        static Shape Make()
        {
            var shape = new RectShape();
            shape.Width.Value = 120;
            shape.Height.Value = 80;
            return shape;
        }

        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap viaEffect = GoldenImageHarness.RenderAtScale(Outlined(Make(), 8, true), Frame, 1f);
            using Bitmap viaPen = GoldenImageHarness.RenderAtScale(Outlined(Make(), 8, false), Frame, 1f);
            float[] effectCoverage = PenCoverage(viaEffect);
            float[] penCoverage = PenCoverage(viaPen);

            int worst = 0;
            for (int i = 1; i < effectCoverage.Length; i++)
            {
                if (Math.Abs(effectCoverage[i] - penCoverage[i]) > Math.Abs(effectCoverage[worst] - penCoverage[worst]))
                    worst = i;
            }

            Assert.That(effectCoverage[worst], Is.EqualTo(penCoverage[worst]).Within(2 / 255f),
                $"pen coverage differs most at ({worst % Frame.Width}, {worst / Frame.Width})");
        });
    }
}
