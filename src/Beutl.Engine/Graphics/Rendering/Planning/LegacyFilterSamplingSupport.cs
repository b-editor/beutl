using System.Collections.Immutable;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Resolves the input region a recorded legacy filter segment reads to produce a requested output region.
/// </summary>
internal static class LegacyFilterSamplingSupport
{
    /// <summary>
    /// Maps <paramref name="output"/> backward through every item of the segment.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the segment is not a pure Skia chain, when an item declares no proven
    /// sampling footprint, or when a mapped region is unusable; the caller must then require the complete
    /// input. A footprint is never inferred from the forward bounds items, which may be narrower than what
    /// the filter reads, as Erode is behind its identity forward map.
    /// </returns>
    public static bool TryResolveSampledInput(ImmutableArray<IFEItem> items, Rect output, out Rect input)
    {
        input = default;
        if (items.IsDefaultOrEmpty || !IsUsable(output))
            return false;

        Rect region = output;
        for (int index = items.Length - 1; index >= 0; index--)
        {
            if (items[index] is not IFEItem_Skia skia
                || !skia.TryTransformSamplingBounds(region, out Rect sampled)
                || !IsUsable(sampled))
            {
                return false;
            }

            region = sampled;
        }

        input = region;
        return true;
    }

    private static bool IsUsable(Rect rect)
        => float.IsFinite(rect.X)
           && float.IsFinite(rect.Y)
           && float.IsFinite(rect.Width)
           && float.IsFinite(rect.Height)
           && rect.Width >= 0
           && rect.Height >= 0;
}
