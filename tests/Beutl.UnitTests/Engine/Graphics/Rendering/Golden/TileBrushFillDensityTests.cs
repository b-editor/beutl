using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// TileBrush fill density: intermediate rasterized at ceil(size * s), tile shader compensated by Scale(1/s).
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

    // A DrawableBrush fill at 2x SSAA must match the same content drawn directly.
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

    // Tiling must use the same logical period at any render scale.
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

    // Diagonal hard-stop stripes for high-frequency density discrimination.
    private static RectShape MakeStripes()
    {
        var stripes = new LinearGradientBrush();
        stripes.StartPoint.CurrentValue = new RelativePoint(0, 0, RelativeUnit.Absolute);
        stripes.EndPoint.CurrentValue = new RelativePoint(11, 7, RelativeUnit.Absolute);
        stripes.SpreadMethod.CurrentValue = GradientSpreadMethod.Repeat;
        stripes.GradientStops.Add(new GradientStop(Colors.White, 0));
        stripes.GradientStops.Add(new GradientStop(Colors.White, 0.5f));
        stripes.GradientStops.Add(new GradientStop(Colors.Black, 0.5f));
        stripes.GradientStops.Add(new GradientStop(Colors.Black, 1));

        var rect = new RectShape();
        rect.AlignmentX.CurrentValue = AlignmentX.Center;
        rect.AlignmentY.CurrentValue = AlignmentY.Center;
        rect.Width.CurrentValue = 160;
        rect.Height.CurrentValue = 160;
        rect.Fill.CurrentValue = stripes;
        return rect;
    }

    // Fine diagonal stripes discriminate: a logical-res intermediate upscaled x2 becomes blocky.
    [Test]
    public void DrawableBrushFill_HighFrequency_SsaaDensity_MatchesDirect()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap direct = GoldenImageHarness.RenderAtScale(
                MakeStripes().ToResource(CompositionContext.Default), Frame, 2f);

            var brush = new DrawableBrush();
            brush.Drawable.CurrentValue = MakeStripes();
            brush.Stretch.CurrentValue = Stretch.Fill;
            brush.TileMode.CurrentValue = TileMode.None;
            brush.DestinationRect.CurrentValue = RelativeRect.Fill;
            var host = new RectShape();
            host.AlignmentX.CurrentValue = AlignmentX.Center;
            host.AlignmentY.CurrentValue = AlignmentY.Center;
            host.Width.CurrentValue = 160;
            host.Height.CurrentValue = 160;
            host.Fill.CurrentValue = brush;
            using Bitmap filled = GoldenImageHarness.RenderAtScale(
                host.ToResource(CompositionContext.Default), Frame, 2f);

            double ssim = ImageMetrics.Ssim(direct, filled);
            double mae = ImageMetrics.MeanAbsoluteError(direct, filled);
            TestContext.WriteLine($"[DrawableBrush fill stripes @2x] vs direct SSIM={ssim:F4} MAE={mae:F4}");
            Assert.That(ssim, Is.GreaterThan(GoldenThresholds.ExactSsimMin),
                $"high-frequency DrawableBrush fill is not device-dense at SSAA: SSIM={ssim:F4}");
        });
    }

    // At s_out == 1.0 the density path is inert: must be deterministic.
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
