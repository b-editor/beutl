using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

/// <summary>
/// <c>BrushRecorder.CreateSourceBounds</c> builds its forward mapping as a <c>_ =&gt; bounds</c> closure declared
/// inside the recorder, so the callback method the factory would otherwise default to is shared by every caller
/// and says nothing about the rectangle the closure captured.
/// </summary>
[TestFixture]
public sealed class BrushSourceBoundsIdentityTests
{
    private static readonly Rect s_domain = new(0, 0, 400, 300);

    [Test]
    public void ResizingABrushedSource_CompilesANewStructuralPlan()
    {
        var shape = new RectShape
        {
            Width = { CurrentValue = 60 },
            Height = { CurrentValue = 40 },
            Fill =
            {
                CurrentValue = new DrawableBrush(
                    new RectShape
                    {
                        Width = { CurrentValue = 20 },
                        Height = { CurrentValue = 20 },
                        Fill = { CurrentValue = Brushes.Red },
                    }),
            },
        };

        using Drawable.Resource resource = shape.ToResource(CompositionContext.Default);
        using var root = new DrawableRenderNode(resource);
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_domain,
                    CacheOptions = RenderCacheOptions.Enabled,
                    Purpose = RenderRequestPurpose.Frame,
                },
                TargetFactory = new CpuTargetFactory(),
            });

        long afterFirst = RecordAndRasterize(shape, resource, root, renderer);
        long afterReplay = RecordAndRasterize(shape, resource, root, renderer);
        shape.Width.CurrentValue = 90;
        long afterResize = RecordAndRasterize(shape, resource, root, renderer);

        Assert.Multiple(() =>
        {
            Assert.That(afterFirst, Is.EqualTo(1));
            Assert.That(afterReplay, Is.EqualTo(1), "an unchanged recording reuses its compiled plan");
            Assert.That(
                afterResize,
                Is.EqualTo(2),
                "the captured source rectangle has to reach the structural key, because the shared closure's "
                + "method identity cannot carry it");
        });
    }

    private static long RecordAndRasterize(
        Drawable shape,
        Drawable.Resource resource,
        DrawableRenderNode root,
        RenderNodeRenderer renderer)
    {
        bool updateOnly = false;
        resource.Update(shape, CompositionContext.Default, ref updateOnly);
        using (var context = new GraphicsContext2D(root, s_domain.Size))
        {
            shape.Render(context, resource);
        }

        renderer.Rasterize().Dispose();
        return renderer.StructuralPlanCacheStatistics.Compilations;
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public int GetMaximumDimension(RenderTargetAllocationDescriptor allocation) => RenderScaleUtilities.MaxBufferDimension;

        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
            => new CpuRenderTarget(allocation.DeviceSize.Width, allocation.DeviceSize.Height);
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear())),
            width,
            height);
}
