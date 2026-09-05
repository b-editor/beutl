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
    /// <param name="maxDimension">
    /// The limit to fit, or <see langword="null"/> for what the device the render will reach can attach.
    /// </param>
    /// <remarks>
    /// The default is the device's limit rather than the engine ceiling: a warning taken against the ceiling
    /// clears an export the device then refuses mid-render, and on a device that attaches 8192 that is every
    /// 4K frame past 2x. It is
    /// <see cref="RenderScaleUtilities.PredictRenderThreadMaxBufferDimension"/> rather than
    /// <see cref="RenderScaleUtilities.ResolveMaxBufferDimension()"/> because this is pre-validation: every
    /// caller asks before the render starts, from a thread that is not the one that will allocate, and the
    /// allocation limit for such a thread is the CPU raster's - the engine ceiling, which is the answer that
    /// admits the refused export.
    /// </remarks>
    public static bool FitsBufferLimit(PixelSize frameSize, int factor, int? maxDimension = null)
    {
        int limit = maxDimension ?? RenderScaleUtilities.PredictRenderThreadMaxBufferDimension();
        (long width, long height) = GetRenderSize(frameSize, factor);
        return width <= limit && height <= limit;
    }
}
