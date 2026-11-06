using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public sealed class FilterEffectCropTests
{
    private static readonly Rect s_sourceBounds = new(24, 20, 40, 32);
    private static readonly Rect s_crop = new(28, 24, 18, 13);

    [Test]
    public void Decal_ShrinksTheBoundsToTheCrop()
    {
        using var context = new FilterEffectContext(s_sourceBounds);

        context.Crop(new Rect(30, 10, 20, 30));

        Assert.Multiple(() =>
        {
            Assert.That(context.CountItems(), Is.EqualTo(1));
            Assert.That(context.Bounds, Is.EqualTo(new Rect(30, 20, 20, 20)));
        });
    }

    [TestCase(GradientSpreadMethod.Pad)]
    [TestCase(GradientSpreadMethod.Repeat)]
    [TestCase(GradientSpreadMethod.Reflect)]
    public void Tiling_FillsTheBoundsTheOutputAlreadyHad(GradientSpreadMethod spreadMethod)
    {
        using var context = new FilterEffectContext(s_sourceBounds);

        context.Crop(s_crop, spreadMethod);

        Assert.That(context.Bounds, Is.EqualTo(s_sourceBounds));
    }

    [Test]
    public void NegativeExtent_CropsTheNormalizedRect()
    {
        using var context = new FilterEffectContext(s_sourceBounds);

        context.Crop(new Rect(s_crop.Right, s_crop.Bottom, -s_crop.Width, -s_crop.Height));

        Assert.That(context.Bounds, Is.EqualTo(s_crop));
    }

    [TestCase(GradientSpreadMethod.Decal)]
    [TestCase(GradientSpreadMethod.Repeat)]
    public void EmptyCrop_RendersNothing(GradientSpreadMethod spreadMethod)
    {
        using FilterEffectRenderNode node = CreateCropFilter(new Rect(30, 26, 0, 8), spreadMethod);
        node.AddChild(CreateSource());
        using RenderNodeRenderer renderer = CreateRenderer(node);

        using RenderNodeRasterization result = renderer.Rasterize();

        Assert.That(result.IsEmpty, Is.True);
    }

    [TestCase(GradientSpreadMethod.Decal)]
    [TestCase(GradientSpreadMethod.Repeat)]
    public void EmptyCrop_StaysEmptyThroughFiltersThatGrowBounds(GradientSpreadMethod spreadMethod)
    {
        using var context = new FilterEffectContext(s_sourceBounds);

        context.Crop(new Rect(30, 26, 0, 8), spreadMethod);
        context.Blur(new Size(4, 4));
        context.DropShadow(new Point(6, 6), new Size(2, 2), Colors.Black);
        context.Dilate(3, 3);

        Assert.That(context.Bounds.IsEmpty, Is.True);
    }

    [Test]
    public void DecalCropMissingTheOutput_StaysEmptyThroughADropShadow()
    {
        using var context = new FilterEffectContext(s_sourceBounds);

        context.Crop(new Rect(100, 100, 10, 10));
        context.DropShadowOnly(new Point(4, 4), new Size(3, 3), Colors.Black);

        Assert.That(context.Bounds.IsEmpty, Is.True);
    }

    [TestCase(GradientSpreadMethod.Decal)]
    [TestCase(GradientSpreadMethod.Repeat)]
    public void EmptyCropFollowedByABlur_RendersNothing(GradientSpreadMethod spreadMethod)
    {
        using FilterEffectRenderNode node = CreateCropFilter(new Rect(30, 26, 0, 8), spreadMethod, new Size(4, 4));
        node.AddChild(CreateSource());
        using RenderNodeRenderer renderer = CreateRenderer(node);

        using RenderNodeRasterization result = renderer.Rasterize();

        Assert.That(result.IsEmpty, Is.True);
    }

    [TestCase(GradientSpreadMethod.Decal)]
    [TestCase(GradientSpreadMethod.Repeat)]
    public void EmptyCrop_BuildsAFilterThatDrawsNothing(GradientSpreadMethod spreadMethod)
    {
        using var context = new FilterEffectContext(s_sourceBounds);
        context.Crop(new Rect(30, 26, 0, 8), spreadMethod);
        using var builder = new SKImageFilterBuilder();
        foreach (IFEItem item in context.GetOrderedItems())
        {
            ((IFEItem_Skia)item).AcceptsDirect(builder);
        }

        // A builder left without a filter draws its source unchanged, which is not a crop to nothing.
        SKImageFilter? filter = builder.GetFilter();
        Assert.That(filter, Is.Not.Null);

        using var surface = SKSurface.Create(new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var paint = new SKPaint { ImageFilter = filter })
        {
            surface.Canvas.SaveLayer(paint);
            surface.Canvas.Clear(SKColors.White);
            surface.Canvas.Restore();
        }

        using SKImage image = surface.Snapshot();
        using SKBitmap bitmap = SKBitmap.FromImage(image);
        Assert.That(bitmap.Pixels.Select(pixel => pixel.Alpha), Has.All.Zero);
    }

    [TestCase(GradientSpreadMethod.Decal)]
    [TestCase(GradientSpreadMethod.Pad)]
    [TestCase(GradientSpreadMethod.Repeat)]
    [TestCase(GradientSpreadMethod.Reflect)]
    public void Crop_ReplaysDirectlyWithTheNestedSkiaCropPixels(GradientSpreadMethod spreadMethod)
    {
        using FilterEffectRenderNode node = CreateCropFilter(s_crop, spreadMethod);
        node.AddChild(CreateSource());
        using RenderNodeRenderer renderer = CreateRenderer(node);
        Rect expectedBounds = spreadMethod == GradientSpreadMethod.Decal ? s_crop : s_sourceBounds;

        using RenderNodeRasterization actual = renderer.Rasterize();
        using Bitmap expected = RenderNestedSkiaReference(expectedBounds, spreadMethod);

        Assert.Multiple(() =>
        {
            Assert.That(actual.Bounds, Is.EqualTo(expectedBounds));
            Assert.That(actual.Bitmap, Is.Not.Null);
            Assert.That(actual.Bitmap!.Width, Is.EqualTo(expected.Width));
            Assert.That(actual.Bitmap.Height, Is.EqualTo(expected.Height));
            Assert.That(
                actual.Bitmap.GetPixelSpan().SequenceEqual(expected.GetPixelSpan()),
                Is.True,
                "Direct replay must be byte-identical to drawing the source under the same Skia crop filter.");
            Assert.That(
                renderer.LastExecutionStatistics.IntermediateTargetAcquisitions,
                Is.Zero,
                "A crop is a pure Skia filter, so it replays into the destination without an intermediate target.");
        });
    }

    [Test]
    public void Repeat_TilesTheCropAcrossTheBounds()
    {
        using FilterEffectRenderNode node = CreateCropFilter(s_crop, GradientSpreadMethod.Repeat);
        node.AddChild(CreateSource());
        using RenderNodeRenderer renderer = CreateRenderer(node);

        using RenderNodeRasterization result = renderer.Rasterize();

        Bitmap bitmap = result.Bitmap ?? throw new AssertionException("The crop rendered no bitmap.");
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        int period = (int)s_crop.Width;
        // Without these a narrow or blank result would repeat trivially.
        Assert.That(bitmap.Width, Is.EqualTo((int)s_sourceBounds.Width));
        Assert.That(pixels.ToArray(), Has.Some.Not.Zero);
        int mismatches = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x + period < bitmap.Width; x++)
            {
                if (!pixels.Slice(((y * bitmap.Width) + x) * 4, 4)
                        .SequenceEqual(pixels.Slice(((y * bitmap.Width) + x + period) * 4, 4)))
                {
                    mismatches++;
                }
            }
        }

        Assert.That(mismatches, Is.Zero, "every pixel must repeat one crop width further along the row");
    }

    [Test]
    public void Decal_SubRegionRequest_ReadsOnlyTheCropInsideTheRegion()
    {
        var sourceBounds = new Rect(0, 0, 400, 400);
        var crop = new Rect(100, 100, 100, 100);
        var observed = new List<Rect>();
        using FilterEffectRenderNode filter = CreateCropFilter(crop, GradientSpreadMethod.Decal);
        filter.AddChild(ScaleRecordingTestHelper.Source(
            EffectiveScale.At(1),
            sourceBounds,
            session => observed.Add(session.RequiredRegion)));
        using RenderNodeRenderer renderer = CreateRenderer(filter, requestedRegion: new Rect(150, 150, 200, 200));

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.That(observed, Is.EqualTo(new[] { new Rect(150, 150, 50, 50) }));
    }

    [Test]
    public void Repeat_SubRegionRequest_ReadsTheWholeCrop()
    {
        var sourceBounds = new Rect(0, 0, 400, 400);
        var crop = new Rect(100, 100, 100, 100);
        var observed = new List<Rect>();
        using FilterEffectRenderNode filter = CreateCropFilter(crop, GradientSpreadMethod.Repeat);
        filter.AddChild(ScaleRecordingTestHelper.Source(
            EffectiveScale.At(1),
            sourceBounds,
            session => observed.Add(session.RequiredRegion)));
        using RenderNodeRenderer renderer = CreateRenderer(filter, requestedRegion: new Rect(0, 0, 50, 50));

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        // A tile anywhere in the output reads from anywhere in the crop.
        Assert.That(observed, Is.EqualTo(new[] { crop }));
    }

    // A group hands the effect one input per child, and the chain runs on each of them. The crop has to cut each where it
    // cuts a single input (#4076). The second input lies outside the crop, so it keeps nothing of its own. A tiling mode
    // is compared inside the crop only: the inputs arrive cut to what the crop reads, and how far its tiles then reach is
    // a question of its own (#4088).
    [TestCase(GradientSpreadMethod.Decal)]
    [TestCase(GradientSpreadMethod.Pad)]
    [TestCase(GradientSpreadMethod.Repeat)]
    [TestCase(GradientSpreadMethod.Reflect)]
    public void Crop_cuts_each_of_several_inputs_where_it_cuts_one(GradientSpreadMethod spreadMethod)
    {
        using Brush.Resource gradient = CreateGradient();
        var one = RasterizeCrop(CreateCropFilter(s_crop, spreadMethod), new RectangleRenderNode(s_sourceBounds, gradient, null));
        var two = RasterizeCrop(
            CreateCropFilter(s_crop, spreadMethod),
            new RectangleRenderNode(s_sourceBounds, gradient, null),
            new RectangleRenderNode(s_sourceBounds.Translate(new Vector(s_sourceBounds.Width + 8, 0)), gradient, null));

        AssertDrawsTheSame(one, two, spreadMethod == GradientSpreadMethod.Decal ? null : s_crop);
    }

    // A custom effect before the crop keeps a single input off the direct path, and leaves its target with a frame of its
    // own. Away from the origin, the crop has to cut it where it cuts the input alone.
    [TestCase(GradientSpreadMethod.Decal)]
    [TestCase(GradientSpreadMethod.Repeat)]
    public void Crop_after_a_custom_effect_cuts_where_it_cuts_alone(GradientSpreadMethod spreadMethod)
    {
        var offset = new Vector(200, 100);
        Rect source = s_sourceBounds.Translate(offset), crop = s_crop.Translate(offset);
        using Brush.Resource gradient = CreateGradient();
        var alone = RasterizeCrop(CreateCropFilter(crop, spreadMethod), new RectangleRenderNode(source, gradient, null));
        var after = RasterizeCrop(
            CreateCropFilter(crop, spreadMethod, customFirst: true), new RectangleRenderNode(source, gradient, null));

        AssertDrawsTheSame(alone, after);
    }

    // A rectangle on whole pixels, so that drawing it straight and drawing what it rasterized to agree, with a colour
    // that changes in both directions, so that a crop off by a pixel shows.
    private static Brush.Resource CreateGradient()
    {
        var gradient = new LinearGradientBrush
        {
            StartPoint = { Value = new RelativePoint(0, 0, RelativeUnit.Relative) },
            EndPoint = { Value = new RelativePoint(1, 1, RelativeUnit.Relative) },
        };
        gradient.GradientStops.Add(new GradientStop(Colors.Red, 0));
        gradient.GradientStops.Add(new GradientStop(Colors.Blue, 1));
        return (Brush.Resource)gradient.ToResource(CompositionContext.Default);
    }

    // The bounds and linear premultiplied BGRA bytes, one array per row.
    private static (Rect Bounds, byte[][] Rows) RasterizeCrop(FilterEffectRenderNode node, params RenderNode[] sources)
    {
        using (node)
        {
            foreach (RenderNode source in sources)
                node.AddChild(source);
            using RenderNodeRenderer renderer = CreateRenderer(node);
            using RenderNodeRasterization rasterization = renderer.Rasterize();
            Bitmap bitmap = rasterization.Bitmap ?? throw new AssertionException("The crop rendered no bitmap.");
            using Bitmap bytes = bitmap.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb);
            return (rasterization.Bounds, [.. Enumerable.Range(0, bytes.Height).Select(y => bytes.GetRow(y).ToArray())]);
        }
    }

    // Asserts that actual draws what expected draws where expected draws it, within rounding, and nothing elsewhere; only
    // inside within when it is given.
    private static void AssertDrawsTheSame(
        (Rect Bounds, byte[][] Rows) expected, (Rect Bounds, byte[][] Rows) actual, Rect? within = null)
    {
        int offsetX = (int)MathF.Round(expected.Bounds.X - actual.Bounds.X);
        int offsetY = (int)MathF.Round(expected.Bounds.Y - actual.Bounds.Y);
        int largest = 0, largestX = 0, largestY = 0, kept = 0;
        for (int y = 0; y < actual.Rows.Length; y++)
        {
            for (int x = 0; x < actual.Rows[y].Length / 4; x++)
            {
                if (within is { } area && !area.Contains(new Point(actual.Bounds.X + x + 0.5f, actual.Bounds.Y + y + 0.5f)))
                    continue;

                int expectedX = x - offsetX, expectedY = y - offsetY;
                bool inExpected = expectedY >= 0 && expectedY < expected.Rows.Length
                                  && expectedX >= 0 && expectedX < expected.Rows[expectedY].Length / 4;
                for (int channel = 0; channel < 4; channel++)
                {
                    int want = inExpected ? expected.Rows[expectedY][(expectedX * 4) + channel] : 0;
                    int difference = Math.Abs(actual.Rows[y][(x * 4) + channel] - want);
                    if (difference > largest)
                        (largest, largestX, largestY) = (difference, x, y);
                }

                if (inExpected && expected.Rows[expectedY][(expectedX * 4) + 3] > 0)
                    kept++;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(kept, Is.Positive, "the crop must keep something");
            Assert.That(largest, Is.LessThanOrEqualTo(2),
                $"the result differs by {largest} at ({largestX + actual.Bounds.X}, {largestY + actual.Bounds.Y}); "
                + $"bounds {actual.Bounds}, expected {expected.Bounds}");
        });
    }

    private static FilterEffectRenderNode CreateCropFilter(
        Rect crop, GradientSpreadMethod spreadMethod, Size blur = default, bool customFirst = false)
        => new(new CropEffect(crop, spreadMethod, blur, customFirst).ToResource(CompositionContext.Default));

    private static EllipseRenderNode CreateSource()
        => new(s_sourceBounds, Brushes.Resource.White, null);

    private static RenderNodeRenderer CreateRenderer(RenderNode node, Rect? requestedRegion = null)
        => new(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            OutputScale = 1,
            MaxWorkingScale = 4,
            RequestedRegion = requestedRegion,
            CacheOptions = RenderCacheOptions.Disabled,
            Purpose = RenderRequestPurpose.Frame,
        }, new CpuRenderTargetFactory());

    private static Bitmap RenderNestedSkiaReference(Rect outputBounds, GradientSpreadMethod spreadMethod)
    {
        PixelRect deviceBounds = PixelRect.FromRect(outputBounds, 1);
        using RenderTarget target = new CpuRenderTarget(deviceBounds.Size);
        using var canvas = new ImmediateCanvas(
            target,
            RenderIntent.Preview,
            density: 1,
            maxWorkingScale: 4,
            logicalSize: deviceBounds.ToRect(1).Size);
        canvas.Clear();

        using SKImageFilter crop = SKImageFilter.CreateCrop(s_crop.ToSKRect(), spreadMethod.ToSKShaderTileMode());
        using var paint = new SKPaint { ImageFilter = crop };
        Rect rasterBounds = deviceBounds.ToRect(1);
        using (canvas.PushTransform(Matrix.CreateTranslation(-rasterBounds.X, -rasterBounds.Y)))
        using (canvas.PushBlendMode(BlendMode.SrcOver))
        using (canvas.PushTransform(Matrix.Identity))
        using (canvas.PushPaint(paint, s_sourceBounds))
        {
            canvas.DrawEllipse(s_sourceBounds, Brushes.Resource.White, null);
        }

        return target.Snapshot();
    }

    [SuppressResourceClassGeneration]
    private sealed partial class CropEffect(Rect crop, GradientSpreadMethod spreadMethod, Size blur, bool customFirst)
        : FilterEffect
    {
        private void ApplyEffect(FilterEffectContext context)
        {
            if (customFirst)
                context.CustomEffect(0, static (_, _) => { }, static (_, bounds) => bounds);
            context.Crop(crop, spreadMethod);
            if (blur.Width > 0 || blur.Height > 0)
                context.Blur(blur);
        }

        public override Resource ToResource(CompositionContext context)
        {
            var resource = new Resource();
            bool versionBumped = false;
            resource.Reconcile(this, context, ref versionBumped);
            return resource;
        }

        public new sealed class Resource : FilterEffect.Resource
        {
            public Resource()
            {
            }

            protected override void ApplyTo(FilterEffectContext context)
                => ((CropEffect)RequireOriginal()).ApplyEffect(context);
        }
    }
}
