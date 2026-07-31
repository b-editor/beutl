using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Planning;

[TestFixture]
[NonParallelizable]
public sealed class MaterializedInputCompositeTests
{
    private static readonly Rect s_sourceBounds = new(0, 0, 7, 5);

    [Test]
    public void ExternalInput_WithExactOneToOneMapping_PreservesSourcePixelBytes()
    {
        using var source = new CpuRenderTarget(7, 5);
        FillHighFrequencyPattern(source);
        using Bitmap expected = source.Snapshot();

        using Bitmap actual = RenderExternalInput(
            source,
            sourceDensity: 1,
            destinationDensity: 1,
            destinationSize: new PixelSize(7, 5),
            transform: Matrix.Identity);

        Assert.That(
            actual.GetPixelSpan().SequenceEqual(expected.GetPixelSpan()),
            Is.True,
            "An exact external input must retain every source pixel byte.");
    }

    [Test]
    public void ExternalInput_WithDifferentDestinationDensity_UsesScaledComposite()
    {
        using var source = new CpuRenderTarget(7, 5);
        FillHighFrequencyPattern(source);
        var destinationSize = new PixelSize(14, 10);

        using Bitmap expected = RenderScaledReference(
            source,
            destinationDensity: 2,
            destinationSize,
            transform: Matrix.Identity);
        using Bitmap actual = RenderExternalInput(
            source,
            sourceDensity: 1,
            destinationDensity: 2,
            destinationSize,
            transform: Matrix.Identity);

        Assert.That(
            actual.GetPixelSpan().SequenceEqual(expected.GetPixelSpan()),
            Is.True,
            "A density mismatch must retain the Mitchell-scaled fallback.");
    }

    [Test]
    public void ExternalInput_WithFractionalTransform_UsesScaledComposite()
    {
        using var source = new CpuRenderTarget(7, 5);
        FillHighFrequencyPattern(source);
        var destinationSize = new PixelSize(9, 7);
        Matrix transform = Matrix.CreateTranslation(0.5f, 0.25f);

        using Bitmap expected = RenderScaledReference(
            source,
            destinationDensity: 1,
            destinationSize,
            transform);
        using Bitmap actual = RenderExternalInput(
            source,
            sourceDensity: 1,
            destinationDensity: 1,
            destinationSize,
            transform);

        Assert.That(
            actual.GetPixelSpan().SequenceEqual(expected.GetPixelSpan()),
            Is.True,
            "A fractional device mapping must retain the transformed scaled fallback.");
    }

    [Test]
    public void ExternalInput_PreservesDeclaredDeviceGridPlacementAndApron()
    {
        var bounds = new Rect(2.25f, 1.5f, 7, 5);
        var deviceGridOffset = new Vector(0.5f, 0.25f);
        PixelRect deviceBounds = PixelRect.FromRect(bounds.Translate(deviceGridOffset), 1);
        Rect rasterBounds = deviceBounds.ToRect(1).Translate(-deviceGridOffset);
        using var source = new CpuRenderTarget(deviceBounds.Width, deviceBounds.Height);
        FillHighFrequencyPattern(source);
        var destinationSize = new PixelSize(12, 9);

        using Bitmap expected = RenderScaledReference(
            source,
            destinationDensity: 1,
            destinationSize,
            transform: Matrix.Identity,
            rasterBounds);
        using Bitmap actual = RenderExternalInput(
            source,
            sourceDensity: 1,
            destinationDensity: 1,
            destinationSize,
            transform: Matrix.Identity,
            bounds,
            deviceBounds,
            deviceGridOffset);

        Assert.That(
            actual.GetPixelSpan().SequenceEqual(expected.GetPixelSpan()),
            Is.True,
            "Materialization must preserve the supplied physical footprint and fractional device-grid phase.");
    }

    private static Bitmap RenderExternalInput(
        RenderTarget source,
        float sourceDensity,
        float destinationDensity,
        PixelSize destinationSize,
        Matrix transform,
        Rect? bounds = null,
        PixelRect? deviceBounds = null,
        Vector deviceGridOffset = default)
    {
        Rect sourceBounds = bounds ?? s_sourceBounds;
        PixelRect sourceDeviceBounds = deviceBounds ?? PixelRect.FromRect(sourceBounds, sourceDensity);
        using var node = new MaterializedInputNode(
            source,
            sourceBounds,
            sourceDensity,
            sourceDeviceBounds,
            deviceGridOffset);
        var options = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            targetDomain: sourceBounds,
            outputScale: destinationDensity,
            maxWorkingScale: destinationDensity,
            cachePolicy: RenderCacheOptions.Disabled);
        using var request = new RenderRequest(options);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        using CompiledRenderRequest compiled = new RenderRequestCompiler().Compile(request, graph);
        using var destination = new CpuRenderTarget(destinationSize.Width, destinationSize.Height);
        using var canvas = new ImmediateCanvas(
            destination,
            destinationDensity,
            destinationDensity,
            destinationSize.ToSize(destinationDensity));
        canvas.Clear();
        using var registry = new RenderTargetLeaseRegistry(factory: null);
        using RenderTargetLeaseSession targets =
            registry.BeginSession(RenderIntent.Preview, destination);
        using (canvas.PushTransform(transform))
        {
            new RenderRequestExecutor(targets).Execute(compiled, canvas);
        }

        return destination.Snapshot();
    }

    private static Bitmap RenderScaledReference(
        RenderTarget source,
        float destinationDensity,
        PixelSize destinationSize,
        Matrix transform,
        Rect? rasterBounds = null)
    {
        using var destination = new CpuRenderTarget(destinationSize.Width, destinationSize.Height);
        using var canvas = new ImmediateCanvas(
            destination,
            destinationDensity,
            destinationDensity,
            destinationSize.ToSize(destinationDensity));
        canvas.Clear();
        using (canvas.PushTransform(transform))
        {
            canvas.DrawRenderTargetScaledWithoutFlush(source, rasterBounds ?? s_sourceBounds);
        }

        return destination.Snapshot();
    }

    private static void FillHighFrequencyPattern(RenderTarget target)
    {
        using var paint = new SKPaint
        {
            IsAntialias = false,
        };
        for (int y = 0; y < target.Height; y++)
        {
            for (int x = 0; x < target.Width; x++)
            {
                paint.Color = ((x + y) & 1) == 0
                    ? new SKColor(255, 24, 8, 255)
                    : new SKColor(4, 40, 255, 255);
                target.Value.Canvas.DrawRect(x, y, 1, 1, paint);
            }
        }
        target.Value.Flush();
    }

    private sealed class MaterializedInputNode(
        RenderTarget source,
        Rect bounds,
        float density,
        PixelRect deviceBounds,
        Vector deviceGridOffset) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderResource<RenderTarget> resource = context.Borrow(
                source,
                cacheKey: "materialized-input-composite-source",
                version: 1);
            context.Publish(context.MaterializedInput(
                MaterializedInputDescription.FromRenderTarget(
                    resource,
                    bounds,
                    EffectiveScale.At(density),
                    deviceBounds,
                    deviceGridOffset,
                    RenderHitTestContract.OutputBounds)));
        }
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                    width,
                    height,
                    SKColorType.RgbaF16,
                    SKAlphaType.Premul,
                    SKColorSpace.CreateSrgbLinear()))
                ?? throw new InvalidOperationException("Could not create the CPU test surface."),
            width,
            height);
}
