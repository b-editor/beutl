using System.Collections.Immutable;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Requests;
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

internal sealed record ShaderGraphRenderFragmentPayload(
    ShaderGraphDescription Description,
    FilterEffectWorkingScalePolicy? WorkingScalePolicy = null)
{
    /// <summary>The density <paramref name="input"/> is materialized at for a graph running at <paramref name="density"/>.</summary>
    public static float InputDensity(RenderFragmentReference input, float density)
        => input.EffectiveScale.IsUnbounded ? density : input.EffectiveScale.Value;

    /// <summary>Lowers <paramref name="density"/> until the graph's allocations for <paramref name="input"/> fit.</summary>
    /// <param name="required">The part of the output a request needs, or an empty rectangle while that is not known.</param>
    public float ClampDensity(RenderFragmentReference input, float density, Rect required)
        => ClampDensity(Description, input, density, required);

    private static float ClampDensity(ShaderGraphDescription description, RenderFragmentReference input, float density, Rect required)
        => description.ClampWorkingScale(input.Bounds, density, default,
            sourceSize: PixelRect.FromRect(input.Bounds, InputDensity(input, density)).Size, required: required);

    /// <summary>The density a graph is planned at, before a request says which part of its output it needs.</summary>
    /// <remarks>The output's complete bounds set no limit: it goes into a target of what the request needs, and
    /// <see cref="ClampDensity(RenderFragmentReference, float, Rect)"/> holds the graph's allocations to their budgets.</remarks>
    public static EffectiveScale ResolvePlannedScale(ShaderGraphDescription description, FilterEffectWorkingScalePolicy? policy,
        IReadOnlyList<RenderFragmentReference> inputs, float outputScale, float maxWorkingScale)
    {
        EffectiveScale[] supplies = inputs.SelectToArray(static input => input.EffectiveScale);
        float density = policy is { } declared
            ? declared.Resolve(supplies, inputs.SelectToArray(input => description.AcceptsPartialSource ? default : input.Bounds),
                [default(Rect)], outputScale, maxWorkingScale).Value
            : RenderScaleUtilities.ResolveWorkingScale(supplies, outputScale, maxWorkingScale);
        return EffectiveScale.At(ClampDensity(description, inputs[0], density, Rect.Empty));
    }
}

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
    /// <summary>Whether the segment runs an imperative effect callback.</summary>
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

    /// <summary>
    /// Whether an imperative effect callback in the segment declares where it reads, so that the segment can be handed
    /// only the part of each value a request needs.
    /// </summary>
    public bool HasCallbackFootprint
    {
        get
        {
            if (BoundsItems.IsDefaultOrEmpty)
                return false;

            foreach (IFEItem item in BoundsItems)
            {
                if (item is IFEItem_Custom { DeclaresSamplingBounds: true })
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Whether the segment can be handed only the part of each value a request needs past what Skia filters read: through
    /// a callback that declares where it reads, or a shader graph at its start that finds its source's pixels through
    /// frames.
    /// </summary>
    /// <remarks>
    /// Such a segment depends on all of its stream's values even where it reads little of them. A callback counts its
    /// targets, and a group's shader graph resolves its origin in the bounds of all of them.
    /// </remarks>
    public bool TakesParts
        => HasCallbackFootprint
           || (!BoundsItems.IsDefaultOrEmpty
               && BoundsItems[0] is FEItem_ShaderGraph { Description.AcceptsPartialSource: true });

    /// <summary>
    /// Whether the segment runs an imperative effect callback that lays out its targets in whole device pixels from
    /// where its content sits, so the executor strips the sub-pixel phase of the ambient device grid for it and its
    /// nested input frames.
    /// </summary>
    /// <remarks>
    /// A callback recorded with <see cref="CustomFilterEffectOptions.KeepsPlacement"/> leaves its content where it is,
    /// so a segment that runs only those keeps the phase, and the content stays where it is drawn without the effect.
    /// </remarks>
    public bool StripsDeviceGridPhase
    {
        get
        {
            if (BoundsItems.IsDefaultOrEmpty)
                return false;

            foreach (IFEItem item in BoundsItems)
            {
                if (item is IFEItem_Custom custom
                    && !custom.Options.HasFlag(CustomFilterEffectOptions.KeepsPlacement))
                    return true;
            }

            return false;
        }
    }

    public bool SupportsDirectReplay => !EffectItemSamplingSupport.RequiresCoverage(StreamInputCount, BoundsItems);
}

internal sealed record MaterializedInputRenderFragmentPayload(
    MaterializedInputDescription Description);

internal sealed record TargetCaptureRenderFragmentPayload(
    TargetCaptureDescription Description);

internal sealed record LayerRenderFragmentPayload(
    Rect? Domain, bool DomainIsQueryFootprint = false, RenderHitTestContract? HitTest = null);

internal sealed record TargetLayerScopeRenderFragmentPayload(
    TargetRegion Region, bool ContributesToBackdrop = true, bool PreservesTargetClear = false);

internal sealed record TargetScopeRenderFragmentPayload(
    TargetScopeDescription Description);

internal sealed record RawTargetScopeRenderFragmentPayload(
    RawTargetScopeDescription Description);

internal sealed record RawTargetCommandRenderFragmentPayload(
    RawTargetCommandDescription Description);

internal sealed record TargetCommandRenderFragmentPayload(
    TargetCommandDescription Description,
    IReadOnlyList<RenderInputReadback> InputReadbacks,
    bool ClearsTarget = false);

internal sealed record BuiltInBackdropCaptureRenderFragmentPayload(
    TargetCaptureDescription Description,
    IBuiltInBackdropCaptureSink Identity);
