using System.Collections.Immutable;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

internal sealed record FilterEffectSegmentRenderFragmentPayload(
    RenderResource<FilterEffectContext> Context,
    ImmutableArray<IFEItem> BoundsItems,
    FilterEffectWorkingScalePolicy? WorkingScalePolicy,
    int StreamInputCount)
{
    /// <summary>
    /// Whether the segment runs an imperative effect callback. Such a callback crops and re-lays-out its
    /// targets in whole device pixels, so the executor strips the sub-pixel phase from the ambient device
    /// grid for this segment and for every nested frame that materializes its inputs. Only the ambient
    /// phase is stripped: a callback whose own target bounds carry a fractional device phase still
    /// allocates off the grid, and an input produced by a separate render request keeps that request's
    /// own grid.
    /// </summary>
    public bool HasImperativeItem
    {
        get
        {
            if (BoundsItems.IsDefaultOrEmpty)
                return false;

            // ImmutableArray's own enumerator is a struct; Enumerable.Any would box it on a path the
            // executor walks for every effect-item-filter fragment it runs.
            foreach (IFEItem item in BoundsItems)
            {
                if (item is IFEItem_Custom)
                    return true;
            }

            return false;
        }
    }

    public bool SupportsDirectReplay
        => StreamInputCount == 1
           && !BoundsItems.IsDefaultOrEmpty
           && BoundsItems.All(static item =>
               item is IFEItem_Skia
               {
                   SupportsDirectReplay: true,
                   ResolveBoundsAtExecutionTime: false,
               });
}
