using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.Helpers;

/// <summary>
/// Size math for export supersampling pre-validation. Checks that the supersampled
/// root surface fits the per-axis buffer limit before encoding starts.
/// </summary>
public static class ExportSupersampling
{
    /// <summary>Returns <c>frameSize * max(1, factor)</c>.</summary>
    public static (long Width, long Height) GetRenderSize(PixelSize frameSize, int factor)
    {
        long f = Math.Max(1, factor);
        return (frameSize.Width * f, frameSize.Height * f);
    }

    /// <summary>Whether the supersampled surface fits the per-axis buffer limit on both axes.</summary>
    public static bool FitsBufferLimit(
        PixelSize frameSize, int factor, int maxDimension = RenderNodeContext.MaxBufferDimension)
    {
        (long width, long height) = GetRenderSize(frameSize, factor);
        return width <= maxDimension && height <= maxDimension;
    }
}
