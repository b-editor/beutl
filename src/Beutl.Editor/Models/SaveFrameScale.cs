using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.Models;

/// <summary>
/// Size math for the save-frame scale-choice dialog. Validates that the scaled surface
/// fits the per-axis buffer limit before rendering.
/// </summary>
public static class SaveFrameScale
{
    private const float MinScale = 1f / 64f;

    /// <summary>The selectable output-resolution multipliers offered by the save dialog.</summary>
    public static IReadOnlyList<float> Factors { get; } = [0.5f, 1f, 2f, 4f];

    /// <summary>Returns <c>ceil(frameSize * scale)</c> per axis. Non-positive scales clamp to MinScale.</summary>
    public static (long Width, long Height) GetRenderSize(PixelSize frameSize, float scale)
    {
        double s = MathF.Max(scale, MinScale);
        return ((long)Math.Ceiling(frameSize.Width * s), (long)Math.Ceiling(frameSize.Height * s));
    }

    /// <summary>Whether the scaled surface fits <paramref name="budget"/> on both axes.</summary>
    /// <param name="budget">The budget to fit. Named by the caller rather than resolved here.</param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="budget"/> is <see langword="default"/> and names nothing to fit.
    /// </exception>
    /// <remarks>
    /// Pass <see cref="BufferBudgetScope.Prediction"/>, not the engine ceiling or a UI-thread
    /// <see cref="BufferBudgetScope.Allocation"/>, which is CPU-rastered: both admit saves the device refuses.
    /// </remarks>
    public static bool FitsBufferLimit(PixelSize frameSize, float scale, BufferDimensionBudget budget)
    {
        int limit = budget.MaxDimension;
        (long width, long height) = GetRenderSize(frameSize, scale);
        return width <= limit && height <= limit;
    }

    /// <summary>Whether the scaled surface is at least 1 px on each axis.</summary>
    public static bool ProducesRenderableSurface(PixelSize frameSize, float scale)
    {
        (long width, long height) = GetRenderSize(frameSize, scale);
        return width >= 1 && height >= 1;
    }
}
