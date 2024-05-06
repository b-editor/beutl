using Beutl.Rendering.Cache;

namespace Beutl.Graphics.Rendering;

public class DrawBackdropNode(IBackdrop backdrop, Rect bounds) : DrawNode(bounds), ISupportRenderCache
{
    public IBackdrop Backdrop { get; } = backdrop;

    public bool Equals(IBackdrop backdrop, Rect bounds)
    {
        return Backdrop == backdrop
               && Bounds == bounds;
    }

    public override bool HitTest(Point point)
    {
        return Bounds.Contains(point);
    }

    public override void Render(ImmediateCanvas canvas)
    {
        Backdrop.Draw(canvas);
    }

    void ISupportRenderCache.Accepts(RenderCache cache)
    {
        cache.ReportRenderCount(0);
    }

    void ISupportRenderCache.CreateCache(IImmediateCanvasFactory factory, RenderCache cache, RenderCacheContext context)
    {
    }

    void ISupportRenderCache.RenderWithCache(ImmediateCanvas canvas, RenderCache cache)
    {
    }
}
