using System.Collections.Immutable;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering.Requests;

/// <summary>
/// Resolves the input region a recorded effect-item filter segment reads to produce a requested output region.
/// </summary>
internal static class EffectItemSamplingSupport
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
        return !items.IsDefaultOrEmpty && TryMapBackward(items, 0, output, requireCoverage: false, out input);
    }

    /// <summary>
    /// Maps <paramref name="output"/> backward through the items after <paramref name="index"/>, to the part of its
    /// output the item at <paramref name="index"/> has to produce.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when an item after it is not a Skia filter with a proven sampling footprint, when a
    /// mapped region is unusable, or when what a filter reads does not give it bounds that cover what is needed of
    /// it, as with a tiled crop, whose output follows all of its input; the item then has to produce all of its
    /// output.
    /// </returns>
    public static bool TryResolveStageOutput(IReadOnlyList<IFEItem> items, int index, Rect output, out Rect stageOutput)
        => TryMapBackward(items, index + 1, output, requireCoverage: true, out stageOutput);

    // Generic so that an ImmutableArray is read without boxing it.
    private static bool TryMapBackward<TItems>(TItems items, int first, Rect output, bool requireCoverage, out Rect input)
        where TItems : IReadOnlyList<IFEItem>
    {
        input = default;
        if (!RenderRectValidation.IsFiniteNonNegative(output))
            return false;

        Rect region = output;
        for (int index = items.Count - 1; index >= first; index--)
        {
            if (items[index] is not IFEItem_Skia skia
                || !skia.TryTransformSamplingBounds(region, out Rect sampled)
                || !RenderRectValidation.IsFiniteNonNegative(sampled)
                || (requireCoverage && !skia.TransformBounds(sampled).Contains(region)))
            {
                return false;
            }

            region = sampled;
        }

        input = region;
        return true;
    }
}
