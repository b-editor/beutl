using Beutl.Rendering.Cache;

namespace Beutl.Graphics.Rendering;

public sealed class BlendModeNode : ContainerNode, ISupportRenderCache
{
    public BlendModeNode(BlendMode blendMode)
    {
        BlendMode = blendMode;
    }

    public BlendMode BlendMode { get; }

    public bool Equals(BlendMode blendMode)
    {
        return BlendMode == blendMode;
    }

    public override void Render(ImmediateCanvas canvas)
    {
        using (canvas.PushBlendMode(BlendMode))
        {
            base.Render(canvas);
        }
    }

    void ISupportRenderCache.Accepts(RenderCache cache)
    {
        cache.ReportRenderCount(0);
    }

    void ISupportRenderCache.RenderForCache(ImmediateCanvas canvas, RenderCache cache)
    {
        Render(canvas);
    }

    void ISupportRenderCache.RenderWithCache(ImmediateCanvas canvas, RenderCache cache)
    {
        Render(canvas);
    }

    Rect ISupportRenderCache.TransformBoundsForCache(RenderCache cache)
    {
        return Bounds;
    }
}
