using System.Runtime.InteropServices;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Composite;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
[NonParallelizable]
public sealed class PixelSortEffectSynchronizationTests
{
    private static readonly Rect s_bounds = new(0, 0, 16, 12);

    [Test]
    [Category("GpuPassFusionGpu")]
    public void PixelSort_WaitsForTheSourceBeforeSamplingItFromVulkan()
    {
        IGraphicsContext context = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = RenderTarget.Create((int)s_bounds.Width, (int)s_bounds.Height)
                ?? throw new InvalidOperationException("Could not create the GPU pixel-sort source.");
            Assert.That(source.Texture, Is.Not.Null);
            // Left unsubmitted on purpose: the effect boundary reuses this buffer instead of
            // re-materializing it, so nothing else orders these draws against the Vulkan passes.
            DrawUnsortedBars(source);

            var flushes = new List<ImmediateCanvasFlushKind>();
            using (ImmediateCanvas.ObserveFlushes(flushes.Add))
                ApplyPixelSort(source).Dispose();

            // Where Skia draws through Metal, the shared timeline holds the Vulkan pass behind Skia's submitted
            // work on the GPU; everywhere else the source is submitted and waited for on the CPU.
            ImmediateCanvasFlushKind expected = context is CompositeContext { Timeline: not null }
                ? ImmediateCanvasFlushKind.PrepareForSamplingSubmit
                : ImmediateCanvasFlushKind.PrepareForSampling;
            Assert.That(
                flushes.Count(item => item == expected),
                Is.EqualTo(1),
                "Reading a Skia-owned texture from a separate Vulkan submission must order it behind Skia's writes.");
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void PixelSort_SortsAnUnsubmittedSourceInsteadOfReturningIt()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = RenderTarget.Create((int)s_bounds.Width, (int)s_bounds.Height)
                ?? throw new InvalidOperationException("Could not create the GPU pixel-sort source.");
            DrawUnsortedBars(source);

            using RenderTarget result = ApplyPixelSort(source);
            using Bitmap sorted = result.Snapshot();
            using Bitmap original = source.Snapshot();

            Assert.That(
                sorted.GetPixelSpan().SequenceEqual(original.GetPixelSpan()),
                Is.False,
                "An empty read of the source makes every pixel an anchor, which hands back the unsorted image.");
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void PixelSort_ReadsHeavySkiaWorkOnlyAfterItFinishes()
    {
        // The bar fixture finishes drawing long before a Vulkan pass could start, so it cannot tell an ordered
        // hand-off from a racing one. Here Skia's queue is still busy with another target when the source is
        // drawn behind it, so a pass reading the source early sorts a partly drawn image that differs from run
        // to run.
        var bounds = new Rect(0, 0, 1280, 720);
        IGraphicsContext context = VulkanTestEnvironment.EnsureAvailable();
        if (context is not CompositeContext)
            Assert.Ignore("Skia shares the Vulkan queue here, so queue order already rules this race out.");

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget busy = RenderTarget.Create((int)bounds.Width, (int)bounds.Height)
                ?? throw new InvalidOperationException("Could not create the GPU busy target.");
            byte[]? first = null;
            for (int run = 0; run < 32; run++)
            {
                DrawBlurredCircles(busy, bounds);
                busy.Value.Flush(true, false);

                using RenderTarget source = RenderTarget.Create((int)bounds.Width, (int)bounds.Height)
                    ?? throw new InvalidOperationException("Could not create the GPU pixel-sort source.");
                DrawBlurredCircles(source, bounds);

                using RenderTarget result = ApplyPixelSort(source, bounds, thresholdMin: 40f, thresholdMax: 60f);
                using Bitmap sorted = result.Snapshot();
                byte[] pixels = MemoryMarshal.AsBytes(sorted.GetPixelSpan()).ToArray();
                if (first is null)
                {
                    first = pixels;
                    continue;
                }

                Assert.That(
                    pixels.AsSpan().SequenceEqual(first),
                    Is.True,
                    $"Run {run} sorted different pixels: the Vulkan pass read the source before Skia finished it.");
            }
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void PixelSort_DoesNotAllocateDepthTextures()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = RenderTarget.Create((int)s_bounds.Width, (int)s_bounds.Height)
                ?? throw new InvalidOperationException("Could not create the GPU pixel-sort source.");
            DrawUnsortedBars(source);

            var allocations = new List<TextureFormat>();
            using (VulkanContext.ObserveTextureAllocations(allocations.Add))
                ApplyPixelSort(source).Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(
                    allocations,
                    Does.Contain(TextureFormat.RGBA16Float),
                    "The allocation observer must see the pixel-sort intermediate textures.");
                Assert.That(
                    allocations,
                    Has.None.EqualTo(TextureFormat.Depth32Float),
                    "Pixel-sort fullscreen passes must not allocate unused depth textures.");
            });
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void PixelSort_ReusesItsDestinationAndScratchTargetsAfterWarmup()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            GpuResourceReclaimQueue.FlushAndDrain();
            using RenderTarget source = RenderTarget.Create((int)s_bounds.Width, (int)s_bounds.Height)
                ?? throw new InvalidOperationException("Could not create the GPU pixel-sort source.");
            DrawUnsortedBars(source);
            using var registry = new RenderTargetPool(factory: null);

            List<TextureFormat> firstAllocations = ApplyPooledPixelSort(source, registry);
            Assert.That(GpuResourceReclaimQueue.PendingCount, Is.GreaterThan(0));
            GpuResourceReclaimQueue.FlushAndDrain();
            List<TextureFormat> secondAllocations = ApplyPooledPixelSort(source, registry);

            Assert.Multiple(() =>
            {
                Assert.That(
                    firstAllocations.Count(static format => format == TextureFormat.RGBA16Float),
                    Is.EqualTo(3),
                    "PixelSort must warm one destination and two scratch slots.");
                Assert.That(
                    secondAllocations,
                    Has.None.EqualTo(TextureFormat.RGBA16Float),
                    "The warmed PixelSort invocation must allocate no additional native targets.");
                Assert.That(registry.Statistics.Creates, Is.EqualTo(3));
                Assert.That(registry.Statistics.Reuses, Is.EqualTo(3));
            });
            GpuResourceReclaimQueue.FlushAndDrain();
        });
    }

    // Four opaque bars whose luminance ascends out of order, so any horizontal ascending sort
    // has to move pixels.
    private static void DrawUnsortedBars(RenderTarget target)
    {
        target.BeginDraw();
        SKCanvas canvas = target.Value.Canvas;
        canvas.Clear(SKColors.Blue);
        SKColor[] colors = [SKColors.Blue, SKColors.White, SKColors.Red, SKColors.Green];
        using var paint = new SKPaint { IsAntialias = false };
        for (int i = 0; i < colors.Length; i++)
        {
            paint.Color = colors[i];
            canvas.DrawRect(
                SKRect.Create(i * 4, 0, 4, (float)s_bounds.Height),
                paint);
        }
    }

    // Deterministic content that costs Skia's queue several milliseconds on any GPU.
    private static void DrawBlurredCircles(RenderTarget target, Rect bounds)
    {
        target.BeginDraw();
        SKCanvas canvas = target.Value.Canvas;
        canvas.Clear(SKColors.Black);
        using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 24);
        using var paint = new SKPaint { IsAntialias = true, MaskFilter = blur };
        var random = new Random(1234);
        for (int i = 0; i < 300; i++)
        {
            paint.Color = new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 200);
            canvas.DrawCircle(
                random.Next((int)bounds.Width),
                random.Next((int)bounds.Height),
                40 + random.Next(160),
                paint);
        }
    }

    private static RenderTarget ApplyPixelSort(RenderTarget source)
        => ApplyPixelSort(source, s_bounds, thresholdMin: 0f, thresholdMax: 100f);

    private static RenderTarget ApplyPixelSort(RenderTarget source, Rect bounds, float thresholdMin, float thresholdMax)
    {
        var effect = new PixelSortEffect();
        effect.Direction.CurrentValue = PixelSortDirection.Horizontal;
        effect.SortKey.CurrentValue = PixelSortKey.Luminance;
        effect.ThresholdMin.CurrentValue = thresholdMin;
        effect.ThresholdMax.CurrentValue = thresholdMax;
        effect.Ascending.CurrentValue = true;

        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(bounds);
        context.ApplyTransactional(effect, resource);
        using var targets = new EffectTargets
        {
            new EffectTarget(source, bounds, EffectiveScale.At(1)),
        };
        using var builder = new SKImageFilterBuilder();
        using var executor = new FilterEffectExecutor(
            targets,
            builder,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1,
            deviceGridOffset: default,
            useExecutorManagedCanvas: true);

        executor.Apply(context);

        RenderTarget applied = executor.CurrentTargets.Single().RenderTarget
            ?? throw new InvalidOperationException("The pixel-sort effect produced no target.");
        return applied.ShallowCopy();
    }

    private static List<TextureFormat> ApplyPooledPixelSort(
        RenderTarget source,
        RenderTargetPool registry)
    {
        var effect = new PixelSortEffect();
        effect.Direction.CurrentValue = PixelSortDirection.Horizontal;
        effect.SortKey.CurrentValue = PixelSortKey.Luminance;
        effect.ThresholdMin.CurrentValue = 0f;
        effect.ThresholdMax.CurrentValue = 100f;
        effect.Ascending.CurrentValue = true;

        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(s_bounds);
        context.ApplyTransactional(effect, resource);
        using RenderTargetLeaseSession session = registry.BeginSession(
            RenderIntent.Delivery,
            source);
        using var targets = new EffectTargets
        {
            new EffectTarget(source, s_bounds, EffectiveScale.At(1)),
        };
        using var builder = new SKImageFilterBuilder();
        using var executor = new FilterEffectExecutor(
            targets,
            builder,
            RenderIntent.Delivery,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1,
            deviceGridOffset: default,
            useExecutorManagedCanvas: true,
            renderTargetLeaseSession: session);

        var allocations = new List<TextureFormat>();
        using (VulkanContext.ObserveTextureAllocations(allocations.Add))
        {
            executor.Apply(context);
            using Bitmap completed = executor.CurrentTargets.Single().RenderTarget!.Snapshot();
        }

        return allocations;
    }
}
