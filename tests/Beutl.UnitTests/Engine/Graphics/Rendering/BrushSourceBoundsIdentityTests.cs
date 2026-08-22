using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

/// <summary>
/// Pins that the rectangle a brush source is recorded against is request data, not structural plan identity.
/// </summary>
/// <remarks>
/// The forward bounds mapping is a closure built while recording, so the callback method behind it is shared by
/// every brush and carries nothing about the rectangle that closure captured. That is deliberate: a resize must
/// re-run the plan with new geometry rather than compile a second one, while a change to the shape of the graph
/// still has to compile. Both halves are pinned here because only their combination says the split is correct.
/// </remarks>
[TestFixture]
public sealed class BrushSourceBoundsIdentityTests
{
    private static readonly Rect s_domain = new(0, 0, 400, 300);

    [Test]
    public void ResizingABrushFilledShape_ReusesTheStructuralPlan()
    {
        var shape = new RectShape();
        shape.Width.CurrentValue = 200;
        shape.Height.CurrentValue = 150;
        shape.Fill.CurrentValue = new LinearGradientBrush();
        using var resource = (Drawable.Resource)shape.ToResource(CompositionContext.Default);
        using var root = new DrawableRenderNode(resource);
        using RenderNodeRenderer renderer = CreateRenderer(root);

        long afterFirstSize = RecordAndRasterize(shape, resource, root, renderer);
        shape.Width.CurrentValue = 320;
        shape.Height.CurrentValue = 240;
        long afterResize = RecordAndRasterize(shape, resource, root, renderer);

        Assert.Multiple(() =>
        {
            Assert.That(afterFirstSize, Is.EqualTo(1));
            Assert.That(afterResize, Is.EqualTo(1),
                "Geometry is request data; a resize must re-run the compiled plan, not compile a second one.");
            Assert.That(renderer.StructuralPlanCacheStatistics.Hits, Is.GreaterThan(0));
        });
    }

    [Test]
    public void AddingAFilterEffect_CompilesANewStructuralPlan()
    {
        var shape = new RectShape();
        shape.Width.CurrentValue = 200;
        shape.Height.CurrentValue = 150;
        shape.Fill.CurrentValue = new LinearGradientBrush();
        using var resource = (Drawable.Resource)shape.ToResource(CompositionContext.Default);
        using var root = new DrawableRenderNode(resource);
        using RenderNodeRenderer renderer = CreateRenderer(root);

        long beforeEffect = RecordAndRasterize(shape, resource, root, renderer);
        shape.FilterEffect.CurrentValue = new Blur();
        long afterEffect = RecordAndRasterize(shape, resource, root, renderer);

        Assert.Multiple(() =>
        {
            Assert.That(beforeEffect, Is.EqualTo(1));
            Assert.That(afterEffect, Is.EqualTo(2),
                "A new boundary changes the shape of the graph, which the plan key must separate.");
        });
    }

    private static RenderNodeRenderer CreateRenderer(DrawableRenderNode root)
        => new(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_domain,
                    CacheOptions = RenderCacheOptions.Disabled,
                    Purpose = RenderRequestPurpose.Frame,
                },
                TargetFactory = new CpuTargetFactory(),
            });

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
