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

    private static FilterEffectRenderNode CreateCropFilter(Rect crop, GradientSpreadMethod spreadMethod)
        => new(new CropEffect(crop, spreadMethod).ToResource(CompositionContext.Default));

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
        }, new CpuTargetFactory());

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

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
            => new CpuRenderTarget(allocation.DeviceSize);
    }

    private sealed class CpuRenderTarget(PixelSize size)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                    size.Width,
                    size.Height,
                    SKColorType.RgbaF16,
                    SKAlphaType.Premul,
                    SKColorSpace.CreateSrgbLinear()))
                ?? throw new InvalidOperationException("Could not create the CPU crop test surface."),
            size.Width,
            size.Height);

    [SuppressResourceClassGeneration]
    private sealed partial class CropEffect(Rect crop, GradientSpreadMethod spreadMethod) : FilterEffect
    {
        public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
        {
            context.Crop(crop, spreadMethod);
        }

        public override Resource ToResource(CompositionContext context)
        {
            var resource = new Resource();
            bool updateOnly = false;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }

        public new sealed class Resource : FilterEffect.Resource
        {
            public Resource()
            {
            }
        }
    }
}
