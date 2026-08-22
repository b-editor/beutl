using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public sealed class LegacyFilterRequiredRegionTests
{
    [Test]
    public void SubRegionRequest_RestrictsLegacyFilterSourceToBackwardRegion()
    {
        var sourceBounds = new Rect(0, 0, 400, 400);
        var requestedRegion = new Rect(0, 0, 50, 50);
        var observed = new List<Rect>();
        using FilterEffectRenderNode filter = CreateBlurNode(sigma: 2);
        filter.AddChild(ScaleRecordingTestHelper.Source(
            EffectiveScale.At(1),
            sourceBounds,
            session => observed.Add(session.RequiredRegion)));
        using var renderer = new RenderNodeRenderer(
            filter,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    RequestedRegion = requestedRegion,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
                TargetFactory = new BudgetedCpuTargetFactory(int.MaxValue),
            });

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        // Blur declares a 3σ footprint, so the destination's 50×50 region can only be reached by the
        // matching 6-unit apron of the source.
        Assert.That(observed, Is.EqualTo(new[] { new Rect(0, 0, 56, 56) }));
    }

    [Test]
    public void SubRegionRequest_RestrictsErodeSourceToItsRadiusNeighbourhood()
    {
        var sourceBounds = new Rect(0, 0, 400, 400);
        var requestedRegion = new Rect(0, 0, 50, 50);
        var observed = new List<Rect>();
        using FilterEffectRenderNode filter = CreateErodeNode(radius: 6);
        filter.AddChild(ScaleRecordingTestHelper.Source(
            EffectiveScale.At(1),
            sourceBounds,
            session => observed.Add(session.RequiredRegion)));
        using var renderer = new RenderNodeRenderer(
            filter,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    RequestedRegion = requestedRegion,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
                TargetFactory = new BudgetedCpuTargetFactory(int.MaxValue),
            });

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        // Erode declares an identity output map yet reads the whole radius neighbourhood, so the
        // destination's 50×50 region still needs the source's matching 6-unit apron.
        Assert.That(observed, Is.EqualTo(new[] { new Rect(0, 0, 56, 56) }));
    }

    [Test]
    public void ErodeUnderAPartialRegion_MatchesTheCompleteRenderRestrictedToThatRegion()
    {
        var sourceBounds = new Rect(0, 0, 100, 100);
        var requestedRegion = new Rect(30, 30, 40, 40);

        (Rect completeBounds, float[] completeAlpha, int completeWidth) =
            RasterizeErodedSquare(sourceBounds, radius: 6, requestedRegion: null);
        (Rect partialBounds, float[] partialAlpha, int partialWidth) =
            RasterizeErodedSquare(sourceBounds, radius: 6, requestedRegion);

        Assert.That(partialBounds, Is.EqualTo(requestedRegion));
        Assert.That(completeBounds, Is.EqualTo(sourceBounds));

        var offsetX = (int)(partialBounds.X - completeBounds.X);
        var offsetY = (int)(partialBounds.Y - completeBounds.Y);
        var partialHeight = partialAlpha.Length / partialWidth;
        var mismatches = 0;
        for (int y = 0; y < partialHeight; y++)
        {
            for (int x = 0; x < partialWidth; x++)
            {
                float expected = completeAlpha[((y + offsetY) * completeWidth) + x + offsetX];
                float actual = partialAlpha[(y * partialWidth) + x];
                if (MathF.Abs(expected - actual) > 0.01f)
                    mismatches++;
            }
        }

        Assert.That(mismatches, Is.Zero,
            "a partially requested erode must match the complete render inside the same region");
    }

    [Test]
    public void OversizedElementAtHighScale_RendersWithoutExceedingTheDestinationFootprint()
    {
        const float scale = 4;
        var frame = new PixelSize(400, 300);
        var requestedSizes = new List<PixelSize>();
        using FilterEffectRenderNode filter = CreateBlurNode(sigma: 4);
        filter.AddChild(new RectangleRenderNode(
            new Rect(0, 0, 4200, 4200),
            Brushes.Resource.White,
            null));
        // A materialization that ignores the destination's needs asks for the element's complete
        // 4200×4200 footprint at density 4, which no backend can satisfy.
        var factory = new BudgetedCpuTargetFactory(8192, requestedSizes);
        using var destination = new CpuRenderTarget(
            (int)(frame.Width * scale),
            (int)(frame.Height * scale));
        using var canvas = new ImmediateCanvas(destination, scale, logicalSize: frame.ToSize(1));
        using var renderer = new RenderNodeRenderer(
            filter,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    TargetDomain = new Rect(default, frame.ToSize(1)),
                    OutputScale = scale,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
                TargetFactory = factory,
            });

        renderer.Render(canvas);

        using Bitmap result = destination.Snapshot();
        Assert.Multiple(() =>
        {
            Assert.That(CountCoveredPixels(result), Is.EqualTo(frame.Width * scale * frame.Height * scale),
                "the blurred element must still cover the frame");
            Assert.That(
                requestedSizes.Select(static size => Math.Max(size.Width, size.Height)),
                Is.All.LessThanOrEqualTo(2048),
                "no intermediate may exceed what the destination region needs");
        });
    }

    [Test]
    public void OutOfDomainMorphologyRadius_RecordsNoStageAndLeavesTheDeclaredBoundsAlone(
        [Values(-6f, 0f)] float radius)
    {
        var bounds = new Rect(0, 0, 120, 80);
        using var dilate = new FilterEffectContext(bounds);
        using var erode = new FilterEffectContext(bounds);

        dilate.Dilate(radius, radius);
        erode.Erode(radius, radius);

        Assert.Multiple(() =>
        {
            Assert.That(dilate.CountItems(), Is.Zero,
                "a morphology radius outside the operation's domain must record no stage");
            Assert.That(erode.CountItems(), Is.Zero,
                "a morphology radius outside the operation's domain must record no stage");
            Assert.That(dilate.Bounds, Is.EqualTo(bounds),
                "a pass-through must not deflate the declared output bounds");
            Assert.That(erode.Bounds, Is.EqualTo(bounds));
        });
    }

    [Test]
    public void MixedSignDilateRadius_StillRecordsTheInDomainAxis()
    {
        var bounds = new Rect(0, 0, 120, 80);
        using var context = new FilterEffectContext(bounds);

        context.Dilate(-6, 5);

        Assert.Multiple(() =>
        {
            Assert.That(context.CountItems(), Is.EqualTo(1),
                "clamping is per axis, so an in-domain y radius still describes a real dilate");
            Assert.That(context.Bounds, Is.EqualTo(new Rect(0, -5, 120, 90)),
                "only the in-domain axis may grow the declared output bounds");
        });
    }

    [Test]
    public void NegativeDilateRadius_RasterizesTheCompleteSource(
        [Values(RenderIntent.Preview, RenderIntent.Delivery)] RenderIntent intent)
    {
        var sourceBounds = new Rect(0, 0, 120, 80);

        (Rect passThroughBounds, float[] passThroughAlpha, int passThroughWidth) =
            RasterizeDilatedSquare(sourceBounds, radius: 0, intent);
        (Rect negativeBounds, float[] negativeAlpha, int negativeWidth) =
            RasterizeDilatedSquare(sourceBounds, radius: -6, intent);

        Assert.Multiple(() =>
        {
            Assert.That(negativeBounds, Is.EqualTo(sourceBounds),
                "a dilate can never delete source content, so a negative radius must be a pass-through");
            Assert.That(negativeWidth, Is.EqualTo(passThroughWidth));
            Assert.That(passThroughBounds, Is.EqualTo(sourceBounds));
        });
        Assert.That(negativeAlpha, Is.EqualTo(passThroughAlpha).Within(0.01f));
    }

    [Test]
    public void NegativeDilateRadiusWiderThanHalfTheSource_DoesNotFailADeliveryRender()
    {
        var sourceBounds = new Rect(0, 0, 120, 80);

        // A radius past half the shorter side is where an unclamped deflation turns the declared
        // output negative-extent, which the flush reports as a non-allocatable delivery failure.
        (Rect bounds, float[] alpha, _) = RasterizeDilatedSquare(sourceBounds, radius: -21, RenderIntent.Delivery);

        Assert.Multiple(() =>
        {
            Assert.That(bounds, Is.EqualTo(sourceBounds));
            Assert.That(alpha, Has.Some.GreaterThan(0.01f), "the frame must not be blank");
        });
    }

    private static FilterEffectRenderNode CreateBlurNode(float sigma)
    {
        var blur = new Blur();
        blur.Sigma.CurrentValue = new Size(sigma, sigma);
        return new FilterEffectRenderNode(blur.ToResource(CompositionContext.Default));
    }

    private static FilterEffectRenderNode CreateDilateNode(float radius)
    {
        var dilate = new Dilate();
        dilate.RadiusX.CurrentValue = radius;
        dilate.RadiusY.CurrentValue = radius;
        return new FilterEffectRenderNode(dilate.ToResource(CompositionContext.Default));
    }

    private static (Rect Bounds, float[] Alpha, int Width) RasterizeDilatedSquare(
        Rect sourceBounds,
        float radius,
        RenderIntent intent)
    {
        using FilterEffectRenderNode filter = CreateDilateNode(radius);
        filter.AddChild(new RectangleRenderNode(sourceBounds, Brushes.Resource.White, null));
        using var renderer = new RenderNodeRenderer(
            filter,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = intent,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
                TargetFactory = new BudgetedCpuTargetFactory(int.MaxValue),
            });

        using RenderNodeRasterization rasterization = renderer.Rasterize();
        Bitmap bitmap = rasterization.Bitmap
                        ?? throw new InvalidOperationException("The dilate render produced no bitmap.");

        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        var alpha = new float[bitmap.Width * bitmap.Height];
        for (int index = 0; index < alpha.Length; index++)
            alpha[index] = (float)BitConverter.UInt16BitsToHalf(pixels[(index * 4) + 3]);

        return (rasterization.Bounds, alpha, bitmap.Width);
    }

    private static FilterEffectRenderNode CreateErodeNode(float radius)
    {
        var erode = new Erode();
        erode.RadiusX.CurrentValue = radius;
        erode.RadiusY.CurrentValue = radius;
        return new FilterEffectRenderNode(erode.ToResource(CompositionContext.Default));
    }

    private static (Rect Bounds, float[] Alpha, int Width) RasterizeErodedSquare(
        Rect sourceBounds,
        float radius,
        Rect? requestedRegion)
    {
        using FilterEffectRenderNode filter = CreateErodeNode(radius);
        filter.AddChild(new RectangleRenderNode(sourceBounds, Brushes.Resource.White, null));
        using var renderer = new RenderNodeRenderer(
            filter,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    RequestedRegion = requestedRegion,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
                TargetFactory = new BudgetedCpuTargetFactory(int.MaxValue),
            });

        using RenderNodeRasterization rasterization = renderer.Rasterize();
        Bitmap bitmap = rasterization.Bitmap
                        ?? throw new InvalidOperationException("The erode render produced no bitmap.");
        Assert.That(bitmap.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));

        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        var alpha = new float[bitmap.Width * bitmap.Height];
        for (int index = 0; index < alpha.Length; index++)
            alpha[index] = (float)BitConverter.UInt16BitsToHalf(pixels[(index * 4) + 3]);

        return (rasterization.Bounds, alpha, bitmap.Width);
    }

    private static long CountCoveredPixels(Bitmap bitmap)
    {
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        long count = 0;
        for (int index = 0; index + 3 < pixels.Length; index += 4)
        {
            float alpha = (float)BitConverter.UInt16BitsToHalf(pixels[index + 3]);
            // The floor rejects NaN/subnormal/negative-zero alpha while accommodating the
            // bilinear tail a budget-clamped materialization leaves at buffer edges.
            if (float.IsFinite(alpha) && alpha >= 0.01f)
                count++;
        }

        return count;
    }

    private sealed class BudgetedCpuTargetFactory(int maximumDimension, List<PixelSize>? requestedSizes = null)
        : IRenderTargetFactory
    {
        public RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
        {
            PixelSize deviceSize = allocation.DeviceSize;
            requestedSizes?.Add(deviceSize);
            return deviceSize.Width > maximumDimension || deviceSize.Height > maximumDimension
                ? null
                : new CpuRenderTarget(deviceSize.Width, deviceSize.Height);
        }
    }

    private sealed class CpuRenderTarget : RenderTarget
    {
        public CpuRenderTarget(int width, int height)
            : base(CreateSurface(width, height), width, height)
        {
        }

        private static SKSurface CreateSurface(int width, int height)
            => SKSurface.Create(new SKImageInfo(
                   width,
                   height,
                   SKColorType.RgbaF16,
                   SKAlphaType.Premul,
                   SKColorSpace.CreateSrgbLinear()))
               ?? throw new InvalidOperationException("Failed to create a CPU surface.");
    }
}
