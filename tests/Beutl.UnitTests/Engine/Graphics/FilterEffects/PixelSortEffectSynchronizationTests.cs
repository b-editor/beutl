using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
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
        VulkanTestEnvironment.EnsureAvailable();
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

            Assert.That(
                flushes.Count(static item => item == ImmediateCanvasFlushKind.PrepareForSampling),
                Is.EqualTo(1),
                "Reading a Skia-owned texture from a separate Vulkan submission must submit and wait.");
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    [Category(TestCategories.KnownVulkanSkiaLayoutInterop)]
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
    [Category(TestCategories.KnownVulkanSkiaLayoutInterop)]
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

    private static RenderTarget ApplyPixelSort(RenderTarget source)
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
        using var targets = new EffectTargets
        {
            new EffectTarget(source, s_bounds, EffectiveScale.At(1)),
        };
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets,
            builder,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1,
            deviceGridOffset: default,
            useExecutorManagedCanvas: true);

        activator.Apply(context);

        RenderTarget applied = activator.CurrentTargets.Single().RenderTarget
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
        using var activator = new FilterEffectActivator(
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
            activator.Apply(context);
            using Bitmap completed = activator.CurrentTargets.Single().RenderTarget!.Snapshot();
        }

        return allocations;
    }
}
