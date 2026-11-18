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
    /// <param name="streamInputCount">How many stream inputs the segment has, which with the items decides whether what
    /// each filter reads has to give it bounds that cover what is needed of it (<see cref="RequiresCoverage{TItems}"/>).</param>
    public static bool TryResolveSampledInput<TItems>(TItems items, int streamInputCount, Rect output, out Rect input)
        where TItems : IReadOnlyList<IFEItem>
    {
        input = default;
        return items.Count > 0
               && TryMapBackward(
                   items, 0, output, RequiresCoverage(streamInputCount, items), stream: null, out input);
    }

    /// <summary>
    /// Maps <paramref name="output"/> backward as <see cref="TryResolveSampledInput{TItems}"/> does, and also through
    /// custom effects that declare where they read and, at the start of the segment, a shader graph that reads its
    /// source through frames.
    /// </summary>
    /// <param name="stream">
    /// The bounds of all of the stream's values and the density the segment runs at, which a shader graph resolves its
    /// origin in and pads by.
    /// </param>
    /// <remarks>
    /// The region analysis narrows a segment's inputs by this, and the executor hands such a segment each value over it.
    /// The buffers a segment's density is planned for and the output a shader graph in it produces still follow
    /// <see cref="TryResolveSampledInput{TItems}"/> and <see cref="TryResolveStageOutput"/>, so a custom effect keeps the
    /// density planned for all of its input and runs on the part it is given.
    /// </remarks>
    public static bool TryResolveRequiredInput<TItems>(
        TItems items,
        int streamInputCount,
        Rect output,
        EffectItemStream stream,
        out Rect input)
        where TItems : IReadOnlyList<IFEItem>
    {
        input = default;
        return items.Count > 0
               && TryMapBackward(
                   items, 0, output, RequiresCoverage(streamInputCount, items), stream, out input);
    }

    /// <summary>
    /// Whether a segment with these items and this many stream inputs runs through the effect executor, where each filter's
    /// output reaches only as far as the targets it gets: the input it reads then has to give it bounds that cover what is
    /// needed of it, which a tiled crop's rectangle does not. Replayed straight into its destination, the segment fills
    /// what is needed whatever it read.
    /// </summary>
    /// <remarks>
    /// The test <see cref="FilterEffectSegmentRenderFragmentPayload.SupportsDirectReplay"/> makes, so that the region a
    /// request reads and the buffers its density is planned for agree.
    /// </remarks>
    public static bool RequiresCoverage<TItems>(int streamInputCount, TItems items)
        where TItems : IReadOnlyList<IFEItem>
    {
        if (streamInputCount != 1 || items.Count == 0)
            return true;

        for (int index = 0; index < items.Count; index++)
        {
            if (items[index] is not IFEItem_Skia { SupportsDirectReplay: true })
                return true;
        }

        return false;
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
        => TryMapBackward(items, index + 1, output, requireCoverage: true, stream: null, out stageOutput);

    // Generic so that an ImmutableArray is read without boxing it.
    private static bool TryMapBackward<TItems>(
        TItems items,
        int first,
        Rect output,
        bool requireCoverage,
        EffectItemStream? stream,
        out Rect input)
        where TItems : IReadOnlyList<IFEItem>
    {
        input = default;
        if (!RenderRectValidation.IsFiniteNonNegative(output))
            return false;

        Rect region = output;
        for (int index = items.Count - 1; index >= first; index--)
        {
            Rect sampled;
            switch (items[index])
            {
                case IFEItem_Skia skia:
                    if (!skia.TryTransformSamplingBounds(region, out sampled)
                        || !RenderRectValidation.IsFiniteNonNegative(sampled)
                        || (requireCoverage && !skia.TransformBounds(sampled).Contains(region)))
                    {
                        return false;
                    }

                    break;
                // What a custom effect declares it reads already covers what it is asked for, wherever it moves its
                // targets.
                case IFEItem_Custom custom when stream is not null:
                    if (!custom.TryTransformSamplingBounds(region, out sampled)
                        || !RenderRectValidation.IsFiniteNonNegative(sampled))
                    {
                        return false;
                    }

                    break;
                // A graph whose passes find their source's pixels through frames reads only around what it is asked for,
                // about an origin it resolves in the bounds of all of the stream. Those are known only before any item has
                // moved the stream's values apart.
                case FEItem_ShaderGraph graph when stream is { } frame
                                                   && index == 0
                                                   && graph.Description.AcceptsPartialSource:
                    sampled = graph.Description.MapSourceRegion(region, frame.Bounds, frame.Density);
                    if (sampled.IsEmpty || !RenderRectValidation.IsFiniteNonNegative(sampled))
                        return false;

                    break;
                default:
                    return false;
            }

            region = sampled;
        }

        input = region;
        return true;
    }
}

/// <summary>
/// The stream a segment's items read: the union of the complete bounds of its values, and the density the segment runs
/// at.
/// </summary>
internal readonly record struct EffectItemStream(Rect Bounds, float Density);
