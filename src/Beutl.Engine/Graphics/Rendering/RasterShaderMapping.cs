using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

internal static class RasterShaderMapping
{
    public static SKMatrix CreateLocalMatrix(
        float destinationScale,
        float sourceScale,
        Rect destinationRasterBounds,
        Rect sourceRasterBounds)
    {
        float scale = destinationScale / sourceScale;
        float offsetX = (float)(
            -(destinationRasterBounds.X - sourceRasterBounds.X) * destinationScale);
        float offsetY = (float)(
            -(destinationRasterBounds.Y - sourceRasterBounds.Y) * destinationScale);
        return new SKMatrix(
            scale,
            0,
            offsetX,
            0,
            scale,
            offsetY,
            0,
            0,
            1);
    }
}
