using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;

namespace Beutl.Graphics;

public interface IImmediateCanvasFactory
{
    RenderNodeCacheContext? GetCacheContext();

    ImmediateCanvas CreateCanvas(RenderTarget renderTarget);

    RenderTarget? CreateRenderTarget(int width, int height);
}
