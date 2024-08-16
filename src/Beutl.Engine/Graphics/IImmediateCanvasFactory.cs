using Beutl.Graphics.Rendering.Cache;
using SkiaSharp;

namespace Beutl.Graphics;

public interface IImmediateCanvasFactory
{
    RenderCacheContext? GetCacheContext();

    ImmediateCanvas CreateCanvas(SKSurface surface, bool leaveOpen);

    SKSurface? CreateRenderTarget(int width, int height);
}
