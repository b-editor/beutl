using SkiaSharp;

namespace Beutl.Graphics.Effects;

internal interface IFEItem_Skia : IFEItem
{
    void Accepts(FilterEffectActivator activator, SKImageFilterBuilder builder);

    bool SupportsDirectReplay { get; }

    void AcceptsDirect(SKImageFilterBuilder builder);

    /// <summary>
    /// When true, the bounds mapping is resolved from the combined execution-time target
    /// bounds instead of per-target authoring-time bounds, and the item is an
    /// <see cref="IFEItem_DeferredBounds"/>. Recording-time bounds walks must treat such an item as
    /// symbolic rather than call <see cref="IFEItem.TransformBounds"/> on it.
    /// </summary>
    bool ResolveBoundsAtExecutionTime { get; }

    /// <summary>
    /// Maps a requested output region to the input region this item reads while producing it.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the item declares no proven sampling footprint; the caller must then
    /// require the complete input. A footprint is never inferred from <see cref="IFEItem.TransformBounds"/>,
    /// which may legitimately be narrower than what the filter reads.
    /// </returns>
    bool TryTransformSamplingBounds(Rect output, out Rect input);
}
