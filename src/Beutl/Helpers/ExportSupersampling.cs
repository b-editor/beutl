using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.Helpers;

/// <summary>
/// Size math for export supersampling pre-validation. Checks that the supersampled
/// root surface fits the per-axis buffer limit before encoding starts.
/// </summary>
public static class ExportSupersampling
{
    public static (long Width, long Height) GetRenderSize(PixelSize frameSize, int factor)
    {
        long f = Math.Max(1, factor);
        return (frameSize.Width * f, frameSize.Height * f);
    }

    /// <summary>Whether the supersampled surface fits <paramref name="budget"/> on both axes.</summary>
    /// <param name="budget">The budget to fit. Named by the caller rather than resolved here.</param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="budget"/> is <see langword="default"/> and names nothing to fit.
    /// </exception>
    /// <remarks>
    /// Pass <see cref="BufferBudgetScope.Prediction"/>: this runs before the render, off the allocating thread.
    /// Checking against <see cref="BufferDimensionBudget.EngineCeiling"/> clears exports the device refuses.
    /// </remarks>
    public static bool FitsBufferLimit(PixelSize frameSize, int factor, BufferDimensionBudget budget)
    {
        int limit = budget.MaxDimension;
        (long width, long height) = GetRenderSize(frameSize, factor);
        return width <= limit && height <= limit;
    }
}
