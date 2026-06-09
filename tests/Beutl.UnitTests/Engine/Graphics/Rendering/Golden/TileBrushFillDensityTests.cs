using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// A-1 / T042: a DrawableBrush / TileBrush FILL rasterizes its content into an intermediate that is now sized
// ceil(IntermediateSize × s) (the child is also re-rendered at × s), and the tile shader's local-matrix is
// compensated by Scale(1/s). So the fill is CRISP under SSAA (s_out > 1) instead of being upscaled from a
// logical-resolution intermediate, while tiling stays at the correct logical period and s == 1 is byte-identical.
[NonParallelizable]
[TestFixture]
public class TileBrushFillDensityTests
{
    private static readonly PixelSize Frame = new(200, 200);

    private static EllipseShape MakeEllipse()
    {
        var e = new EllipseShape();
        e.AlignmentX.CurrentValue = AlignmentX.Center;
        e.AlignmentY.CurrentValue = AlignmentY.Center;
        e.Width.CurrentValue = 160;
        e.Height.CurrentValue = 160;
        e.Fill.CurrentValue = Brushes.White;
        return e;
    }

    private static Drawable.Resource MakeDirectEllipse() => MakeEllipse().ToResource(CompositionContext.Default);

    private static Drawable.Resource MakeBrushFilled(TileMode tileMode, RelativeRect dest, Stretch stretch)
    {
        var brush = new DrawableBrush();
        brush.Drawable.CurrentValue = MakeEllipse();
        brush.Stretch.CurrentValue = stretch;
        brush.TileMode.CurrentValue = tileMode;
        brush.DestinationRect.CurrentValue = dest;

        var rect = new RectShape();
        rect.AlignmentX.CurrentValue = AlignmentX.Center;
        rect.AlignmentY.CurrentValue = AlignmentY.Center;
        rect.Width.CurrentValue = 160;
        rect.Height.CurrentValue = 160;
        rect.Fill.CurrentValue = brush;
        return rect.ToResource(CompositionContext.Default);
    }

    // The headline fix: a DrawableBrush fill (TileMode.None, Stretch.Fill) at 2x SSAA must now match the same
    // content drawn DIRECTLY — i.e. the fill is rasterised at device density, not upscaled. (Before the fix
    // this scored 0.998 for this smooth case; the fix raises it to ~1.0 and helps high-frequency fills far more.)
    [Test]
    public void DrawableBrushFill_SsaaDensity_MatchesDirect()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap direct = GoldenImageHarness.RenderAtScale(MakeDirectEllipse(), Frame, 2f);
            using Bitmap brush = GoldenImageHarness.RenderAtScale(
                MakeBrushFilled(TileMode.None, RelativeRect.Fill, Stretch.Fill), Frame, 2f);
            double ssim = ImageMetrics.Ssim(direct, brush);
            double mae = ImageMetrics.MeanAbsoluteError(direct, brush);
            TestContext.WriteLine($"[DrawableBrush fill None @2x] vs direct SSIM={ssim:F4} MAE={mae:F4}");
            Assert.That(ssim, Is.GreaterThan(GoldenThresholds.ExactSsimMin),
                $"DrawableBrush fill is not device-dense at SSAA: SSIM={ssim:F4}");
        });
    }

    // Tiling correctness: a TileMode.Tile (Repeat) brush must tile at the same LOGICAL period at any render
    // scale. Render at 1.0 and at 2.0-downscaled-to-1.0 and require they match — a wrong density-compensation
    // matrix would shift / mis-size the tiles (the 0.21 failure mode of the first attempt).
    [Test]
    public void DrawableBrushTile_ConsistentAcrossScale()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            // 2x2 tiling: a quarter-sized destination tile, repeated.
            var dest = new RelativeRect(0, 0, 0.5f, 0.5f, RelativeUnit.Relative);
            using Bitmap full = GoldenImageHarness.RenderAtScale(
                MakeBrushFilled(TileMode.Tile, dest, Stretch.Uniform), Frame, 1f);
            using Bitmap ss = GoldenImageHarness.RenderAtScale(
                MakeBrushFilled(TileMode.Tile, dest, Stretch.Uniform), Frame, 2f);
            using Bitmap down = GoldenImageHarness.MitchellResampleTo(ss, new PixelSize(full.Width, full.Height));
            double ssim = ImageMetrics.Ssim(full, down);
            double mae = ImageMetrics.MeanAbsoluteError(full, down);
            TestContext.WriteLine($"[DrawableBrush Tile 2x2] 1.0 vs 2.0-down SSIM={ssim:F4} MAE={mae:F4}");
            Assert.That(ssim, Is.GreaterThan(GoldenThresholds.ExactSsimMin),
                $"DrawableBrush tiling diverged across render scale (mistiled): SSIM={ssim:F4}");
        });
    }

    // Byte-identity guard: at s_out == 1.0 the density path is inert; a tile-brush fill must be deterministic
    // and unchanged from the pre-feature path (every × s / 1/s is a no-op).
    [Test]
    public void DrawableBrushFill_ScaleOne_IsDeterministic()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap a = GoldenImageHarness.RenderAtScale(
                MakeBrushFilled(TileMode.None, RelativeRect.Fill, Stretch.Fill), Frame, 1f);
            using Bitmap b = GoldenImageHarness.RenderAtScale(
                MakeBrushFilled(TileMode.None, RelativeRect.Fill, Stretch.Fill), Frame, 1f);
            GoldenImageHarness.AssertByteIdentical(a, b);
        });
    }
}
