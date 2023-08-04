using Beutl.Graphics;

namespace Beutl.Rendering.Cache;

public interface ISupportRenderCache
{
    void Accepts(RenderCache cache);

    Rect TransformBoundsForCache(RenderCache cache);

    void RenderForCache(ImmediateCanvas canvas, RenderCache cache);

    void RenderWithCache(ImmediateCanvas canvas, RenderCache cache);
}
