using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Beutl.Serialization;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// TileBrush fill density: intermediate rasterized at ceil(size * s), tile shader compensated by Scale(1/s).
[NonParallelizable]
[TestFixture]
public class TileBrushFillDensityTests
{
    private static readonly PixelSize Frame = new(200, 200);

    private static EllipseShape MakeEllipse(float size = 160)
    {
        var e = new EllipseShape();
        e.AlignmentX.CurrentValue = AlignmentX.Center;
        e.AlignmentY.CurrentValue = AlignmentY.Center;
        e.Width.CurrentValue = size;
        e.Height.CurrentValue = size;
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

    // Same logical-period invariant as above, but with a non-zero destination origin (non-zero
    // tile translation offset), which the zero-origin cases never reach.
    [Test]
    public void DrawableBrushTile_NonOriginDest_ConsistentAcrossScale()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            // A quarter-sized tile anchored away from the origin, repeated.
            var dest = new RelativeRect(0.1f, 0.15f, 0.5f, 0.5f, RelativeUnit.Relative);
            using Bitmap full = GoldenImageHarness.RenderAtScale(
                MakeBrushFilled(TileMode.Tile, dest, Stretch.Uniform), Frame, 1f);
            using Bitmap ss = GoldenImageHarness.RenderAtScale(
                MakeBrushFilled(TileMode.Tile, dest, Stretch.Uniform), Frame, 2f);
            using Bitmap down = GoldenImageHarness.MitchellResampleTo(ss, new PixelSize(full.Width, full.Height));
            double ssim = ImageMetrics.Ssim(full, down);
            double mae = ImageMetrics.MeanAbsoluteError(full, down);
            TestContext.WriteLine($"[DrawableBrush Tile non-origin] 1.0 vs 2.0-down SSIM={ssim:F4} MAE={mae:F4}");
            Assert.That(ssim, Is.GreaterThan(GoldenThresholds.ExactSsimMin),
                $"non-origin DrawableBrush tiling diverged across render scale (mistiled): SSIM={ssim:F4}");
        });
    }

    // A drawable smaller than the brush destination must stretch to cover it. This only discriminates
    // when the drawable's intrinsic bounds differ from the destination box — every other case here
    // uses a 160x160 drawable in a 160x160 host, where the two coincide.
    [Test]
    public void DrawableBrushFill_ContentSmallerThanDestination_CoversTheWholeDestination()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var brush = new DrawableBrush();
            brush.Drawable.CurrentValue = MakeEllipse(40);
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
                host.ToResource(CompositionContext.Default), Frame, 1f);

            int litWidth = LitWidthOnCentreRow(filled);
            TestContext.WriteLine($"[DrawableBrush 40->160 fill] lit width on the centre row = {litWidth}px");
            Assert.That(litWidth, Is.GreaterThan(150),
                "a Stretch.Fill drawable brush must scale its content to the destination; "
                + "a lit width near the drawable's own 40px means the tile calculator was handed "
                + "the destination box as the source size");
        });
    }

    // A 60x40 source under a target-rewriting effect, filling a 180x120 host through a Stretch.Uniform
    // DrawableBrush. The brush must stretch against what the effect produced, not against the host box.
    private static Drawable.Resource MakeEffectedBrushHost(FilterEffect effect)
    {
        var source = new RectShape();
        source.AlignmentX.CurrentValue = AlignmentX.Center;
        source.AlignmentY.CurrentValue = AlignmentY.Center;
        source.Width.CurrentValue = 60;
        source.Height.CurrentValue = 40;
        source.Fill.CurrentValue = Brushes.White;
        source.FilterEffect.CurrentValue = effect;

        var brush = new DrawableBrush();
        brush.Drawable.CurrentValue = source;
        brush.Stretch.CurrentValue = Stretch.Uniform;
        brush.TileMode.CurrentValue = TileMode.None;
        brush.DestinationRect.CurrentValue = RelativeRect.Fill;

        var host = new RectShape();
        host.AlignmentX.CurrentValue = AlignmentX.Center;
        host.AlignmentY.CurrentValue = AlignmentY.Center;
        host.Width.CurrentValue = 180;
        host.Height.CurrentValue = 120;
        host.Fill.CurrentValue = brush;
        return host.ToResource(CompositionContext.Default);
    }

    // 2x2 tiles with a 6px gap widen the 60x40 source to 66x46; Uniform into 180x120 scales it by
    // min(180/66, 120/46) = 2.6087, so the painted extent must be 172x120, not the source's own 66x46.
    [Test]
    [Category("GpuPassFusionGpu")]
    public void DrawableBrushFill_SourceCarriesSplitEffect_StretchesAgainstTheEffectOutput()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var split = new SplitEffect();
            split.HorizontalDivisions.CurrentValue = 2;
            split.VerticalDivisions.CurrentValue = 2;
            split.HorizontalSpacing.CurrentValue = 6;
            split.VerticalSpacing.CurrentValue = 6;

            using Bitmap filled = GoldenImageHarness.RenderAtScale(MakeEffectedBrushHost(split), Frame, 1f);
            PixelRect painted = PaintedBounds(filled);
            TestContext.WriteLine($"[DrawableBrush split 66x46 -> 180x120] painted = {painted}");
            Assert.Multiple(() =>
            {
                Assert.That(painted.Width, Is.EqualTo(172).Within(3),
                    "the split source must stretch to the destination; a width near the source's own 66px "
                    + "means the brush was handed the host box as its content bounds");
                Assert.That(painted.Height, Is.EqualTo(120).Within(3),
                    "Stretch.Uniform must cover the constraining axis of the destination");
            });
        });
    }

    // The same defect through an effect that only flattens its targets: 60x40 into 180x120 is a clean 3x.
    [Test]
    [Category("GpuPassFusionGpu")]
    public void DrawableBrushFill_SourceCarriesLayerEffect_StretchesAgainstTheEffectOutput()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap filled = GoldenImageHarness.RenderAtScale(
                MakeEffectedBrushHost(new LayerEffect()), Frame, 1f);
            PixelRect painted = PaintedBounds(filled);
            TestContext.WriteLine($"[DrawableBrush layer 60x40 -> 180x120] painted = {painted}");
            Assert.Multiple(() =>
            {
                Assert.That(painted.Width, Is.EqualTo(180).Within(3),
                    "a bounds-preserving effect must leave the brush stretching against the 60x40 source");
                Assert.That(painted.Height, Is.EqualTo(120).Within(3),
                    "a bounds-preserving effect must leave the brush stretching against the 60x40 source");
            });
        });
    }

    // Bounding box of non-black pixels in a black-cleared render.
    private static PixelRect PaintedBounds(Bitmap bitmap)
    {
        int left = int.MaxValue;
        int top = int.MaxValue;
        int right = int.MinValue;
        int bottom = int.MinValue;
        for (int y = 0; y < bitmap.Height; y++)
        {
            ReadOnlySpan<ushort> row = bitmap.GetRow<ushort>(y);
            for (int x = 0; x < bitmap.Width; x++)
            {
                if ((float)BitConverter.UInt16BitsToHalf(row[x * 4]) <= 0.01f) continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        return right < left ? default : new PixelRect(left, top, right - left + 1, bottom - top + 1);
    }

    // First-to-last extent of non-black pixels on the middle scanline of a black-cleared render.
    private static int LitWidthOnCentreRow(Bitmap bitmap)
    {
        ReadOnlySpan<ushort> row = bitmap.GetRow<ushort>(bitmap.Height / 2);
        int first = -1;
        int last = -1;
        for (int x = 0; x < bitmap.Width; x++)
        {
            float luma = (float)BitConverter.UInt16BitsToHalf(row[x * 4]);
            if (luma <= 0.01f) continue;
            if (first < 0) first = x;
            last = x;
        }

        return first < 0 ? 0 : last - first + 1;
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

    // Four solid quadrants: cross hard edges discriminate a mis-scaled or mis-offset density path,
    // while staying smooth enough that an honest 2x resample stays within the exact SSIM band.
    private static Uri CreateQuadrantImageUri(int size)
    {
        using var bitmap = new Bitmap(size, size);
        Span<Bgra8888> px = bitmap.GetPixelSpan<Bgra8888>();
        int half = size / 2;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Color c = (x < half, y < half) switch
                {
                    (true, true) => Colors.Red,
                    (false, true) => Colors.Lime,
                    (true, false) => Colors.Blue,
                    (false, false) => Colors.White,
                };
                px[(y * size) + x] = new Bgra8888(c.R, c.G, c.B, c.A);
            }
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, EncodedImageFormat.Png);
        return UriHelper.CreateBase64DataUri("image/png", stream.ToArray());
    }

    private static Drawable.Resource MakeImageBrushFilled(Uri uri)
    {
        var source = new ImageSource();
        source.ReadFrom(uri);
        var brush = new ImageBrush(source);
        brush.Stretch.CurrentValue = Stretch.Fill;
        brush.TileMode.CurrentValue = TileMode.None;
        brush.DestinationRect.CurrentValue = RelativeRect.Fill;

        var rect = new RectShape();
        rect.AlignmentX.CurrentValue = AlignmentX.Center;
        rect.AlignmentY.CurrentValue = AlignmentY.Center;
        rect.Width.CurrentValue = 160;
        rect.Height.CurrentValue = 160;
        rect.Fill.CurrentValue = brush;
        return rect.ToResource(CompositionContext.Default);
    }

    // ImageBrush feeds a native-density bitmap (contentDensity == 1) through the dense intermediate;
    // a 2x SSAA fill, downscaled, must reproduce the 1x fill (density math carries the source faithfully).
    [Test]
    public void ImageBrushFill_SsaaDensity_ConsistentAcrossScale()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            Uri uri = CreateQuadrantImageUri(160);
            using Bitmap full = GoldenImageHarness.RenderAtScale(MakeImageBrushFilled(uri), Frame, 1f);
            using Bitmap ss = GoldenImageHarness.RenderAtScale(MakeImageBrushFilled(uri), Frame, 2f);
            using Bitmap down = GoldenImageHarness.MitchellResampleTo(ss, new PixelSize(full.Width, full.Height));
            double ssim = ImageMetrics.Ssim(full, down);
            double mae = ImageMetrics.MeanAbsoluteError(full, down);
            TestContext.WriteLine($"[ImageBrush fill @2x] 1.0 vs 2.0-down SSIM={ssim:F4} MAE={mae:F4}");
            Assert.That(ssim, Is.GreaterThan(GoldenThresholds.ExactSsimMin),
                $"ImageBrush fill is not density-consistent across render scale: SSIM={ssim:F4}");
        });
    }
}
