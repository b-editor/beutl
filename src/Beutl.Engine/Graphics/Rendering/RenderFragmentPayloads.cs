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
    /// <summary>
    /// Whether the segment runs an imperative effect callback, which lays out its targets in whole device pixels,
    /// so the executor strips the sub-pixel phase of the ambient device grid for it and its nested input frames.
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
           && BoundsItems.All(static item => item is IFEItem_Skia { SupportsDirectReplay: true });
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
