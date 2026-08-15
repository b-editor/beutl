using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
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

    // Four opaque bars whose luminance ascends out of order, so any horizontal ascending sort
    // has to move pixels.
    private static void DrawUnsortedBars(RenderTarget target)
    {
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
}
