using Beutl.Graphics;

namespace Beutl.Rendering.Cache;

public interface ISupportRenderCache
{
    void Accepts(RenderCache cache);

    void RenderWithCache(ImmediateCanvas canvas, RenderCache cache);

    void CreateCache(IImmediateCanvasFactory factory, RenderCache cache, RenderCacheContext context);
}
