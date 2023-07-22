using SkiaSharp;

namespace Beutl.Graphics;

public interface IImmediateCanvasFactory
{
    ImmediateCanvas CreateCanvas(SKSurface surface, bool leaveOpen);

    SKSurface CreateRenderTarget(int width, int height);
}
