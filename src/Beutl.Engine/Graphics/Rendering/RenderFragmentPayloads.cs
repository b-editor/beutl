using System.Collections.Immutable;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shaders;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

internal sealed record OpacityRenderFragmentPayload(
    float Opacity,
    ShaderDescription FusionDescription);

internal sealed record BlendRenderFragmentPayload(BlendMode BlendMode);

internal sealed record OpacityMaskRenderFragmentPayload(
    RenderResource<Brush.Resource> Mask,
    Rect BrushBounds,
    bool Invert);

internal sealed record ShaderRenderFragmentPayload(
    ShaderDescription Description,
    FilterEffectWorkingScalePolicy? WorkingScalePolicy = null);

internal sealed record GeometryRenderFragmentPayload(
    GeometryDescription Description,
    FilterEffectWorkingScalePolicy? WorkingScalePolicy = null);

internal sealed record OpaqueRenderFragmentPayload(
    OpaqueRenderTopology Topology,
    OpaqueRenderDescription Description,
    IReadOnlyList<RenderInputReadback> InputReadbacks);

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

internal sealed record MaterializedInputRenderFragmentPayload(
    MaterializedInputDescription Description);

internal sealed record TargetCaptureRenderFragmentPayload(
    TargetCaptureDescription Description);

internal sealed record LayerRenderFragmentPayload(Rect? Domain, bool DomainIsQueryFootprint = false);

internal sealed record TargetLayerScopeRenderFragmentPayload(TargetRegion Region);

internal sealed record TargetScopeRenderFragmentPayload(
    TargetScopeDescription Description);

internal sealed record RawTargetScopeRenderFragmentPayload(
    RawTargetScopeDescription Description);

internal sealed record RawTargetCommandRenderFragmentPayload(
    RawTargetCommandDescription Description);

internal sealed record TargetCommandRenderFragmentPayload(
    TargetCommandDescription Description,
    IReadOnlyList<RenderInputReadback> InputReadbacks);

internal sealed record BuiltInBackdropCaptureRenderFragmentPayload(
    TargetCaptureDescription Description,
    object Identity);
