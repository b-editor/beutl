using SkiaSharp;

namespace Beutl.Graphics.Effects;

internal interface IFEItem_Skia : IFEItem
{
    void Accepts(FilterEffectExecutor executor, SKImageFilterBuilder builder);

    bool SupportsDirectReplay { get; }

    /// <summary>
    /// Gets whether the filter reads positions in the coordinates of <see cref="FilterEffectContext.Bounds"/> that
    /// moving its input does not move, as a crop's rectangle is.
    /// </summary>
    /// <remarks>
    /// A flush runs a chain at each target's own origin and then moves the result into place. That holds for a filter
    /// whose output moves with its input: a blur, a shadow's offset, or a matrix, whose move the flush works out from
    /// the bounds. A chain with any other filter runs in the coordinates of the bounds instead.
    /// </remarks>
    bool NeedsBoundsCoordinates { get; }

    /// <summary>
    /// Gets whether the filter reads its input as one picture, as a tiling crop does: each tile repeats what the crop keeps
    /// of every target, across all of them.
    /// </summary>
    /// <remarks>
    /// A flush runs a chain on each target apart, which gives what one picture of them all would for a filter that maps each
    /// point from around it. Several targets are drawn into one before a filter that needs one.
    /// </remarks>
    bool NeedsOneTarget { get; }

    void AcceptsDirect(SKImageFilterBuilder builder);

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
