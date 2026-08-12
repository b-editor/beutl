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
