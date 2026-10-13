using System.Globalization;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering.Requests;

/// <summary>
/// Replays each author-supplied forward mapping (bounds and supply density) of a symbolic fragment over its
/// inputs' recorded metadata and fails if it no longer returns what recording stored.
/// </summary>
/// <remarks>
/// Engine rules are not replayed, since that would compare the engine with itself; hit tests and backward mappings
/// are not either, because no recorded answer exists for them.
/// </remarks>
internal static class SymbolicMetadataCrossCheck
{
    /// <summary>
    /// Verifies the forward bounds mapping of a symbolic fragment against the bounds recording stored.
    /// </summary>
    public static void VerifyForwardBounds(RenderFragmentReference reference)
    {
        if (ReplayRecordedBounds(reference) is not { } replayed || replayed == reference.RecordedBounds)
            return;

        throw new InvalidOperationException(
            "A forward bounds mapping changed between recording and graph-wide metadata resolution. Asked "
            + $"again for the input bounds it was recorded over, it answered {replayed} where recording "
            + $"stored {reference.RecordedBounds}. A bounds mapping must be deterministic over its inputs, "
            + "so this one reads state that moved after the recording that used it.");
    }

    /// <summary>
    /// Verifies the supply-density contract of a symbolic fragment against the density recording stored.
    /// </summary>
    public static void VerifyForwardScale(RenderFragmentReference reference, RenderRequestOptions options)
    {
        if (ReplayRecordedScale(reference, options) is not { } replayed
            || replayed == reference.RecordedEffectiveScale)
        {
            return;
        }

        throw new InvalidOperationException(
            "A supply-density contract changed between recording and graph-wide metadata resolution. Asked "
            + $"again for the input densities it was recorded over, it answered {Describe(replayed)} where "
            + $"recording stored {Describe(reference.RecordedEffectiveScale)}. A density contract must be "
            + "deterministic over its inputs, so this one reads state that moved after the recording that "
            + "used it.");
    }

    private static bool DeclaresAmbientTransform(RenderFragmentReference reference)
        => reference.Payload is TargetScopeRenderFragmentPayload
        {
            Description.AmbientTransform.DependsOnAmbient: true,
        };

    private static Rect? ReplayRecordedBounds(RenderFragmentReference reference)
    {
        // An owning-domain fragment and a scope composing against the ambient carry placeholder bounds rather
        // than an answer an author mapping gave, so there is nothing to replay.
        if (reference.BoundsRequirement == RenderFragmentBoundsRequirement.OwningTargetDomain
            || DeclaresAmbientTransform(reference))
        {
            return null;
        }

        switch (reference.Kind)
        {
            case RenderFragmentKind.Shader:
                return ((ShaderRenderFragmentPayload)reference.Payload!).Description.Bounds
                    .TransformBounds(RecordedBoundsOf(reference, 0));
            case RenderFragmentKind.Geometry:
                return ((GeometryRenderFragmentPayload)reference.Payload!).Description.Bounds
                    .TransformBounds(RecordedBoundsOf(reference, 0));
            case RenderFragmentKind.OpaqueSource:
            case RenderFragmentKind.OpaqueMap:
            case RenderFragmentKind.OpaqueCombine:
            case RenderFragmentKind.OpaqueExpand:
                return ((OpaqueRenderFragmentPayload)reference.Payload!).Description.Bounds
                    .TransformBounds(RecordedInputBounds(reference, reference.Inputs.Length));
            case RenderFragmentKind.FilterEffectSegment:
                return ReplayEffectItemBounds(reference);
            case RenderFragmentKind.TargetScope:
                return ((TargetScopeRenderFragmentPayload)reference.Payload!).Description.Bounds
                    .TransformBounds(RecordedBoundsOf(reference, 0));
            case RenderFragmentKind.RawTargetScope:
                return ((RawTargetScopeRenderFragmentPayload)reference.Payload!).Description.Bounds
                    .TransformBounds(RecordedBoundsOf(reference, 0));
            default:
                return null;
        }
    }

    private static Rect ReplayEffectItemBounds(RenderFragmentReference reference)
    {
        var payload = (FilterEffectSegmentRenderFragmentPayload)reference.Payload!;
        Rect bounds = default;
        for (int index = 0; index < payload.StreamInputCount; index++)
            bounds = bounds.Union(reference.Inputs[index].RecordedBounds);
        foreach (IFEItem item in payload.BoundsItems)
            bounds = item.TransformBounds(bounds);
        return bounds;
    }

    private static EffectiveScale? ReplayRecordedScale(
        RenderFragmentReference reference,
        RenderRequestOptions options)
    {
        // The same placeholder rule the forward bounds follow: this scope's density contract reads the matrix
        // resolution supplies, so recording stored the engine's answer over a provisional one.
        if (DeclaresAmbientTransform(reference))
            return null;

        switch (reference.Kind)
        {
            case RenderFragmentKind.Shader:
                return ReplayWorkingScalePolicy(
                    reference,
                    ((ShaderRenderFragmentPayload)reference.Payload!).WorkingScalePolicy,
                    options);
            case RenderFragmentKind.Geometry:
                return ReplayWorkingScalePolicy(
                    reference,
                    ((GeometryRenderFragmentPayload)reference.Payload!).WorkingScalePolicy,
                    options);
            case RenderFragmentKind.FilterEffectSegment:
                return ReplayEffectItemScale(reference, options);
            case RenderFragmentKind.OpaqueSource:
            case RenderFragmentKind.OpaqueMap:
            case RenderFragmentKind.OpaqueCombine:
            case RenderFragmentKind.OpaqueExpand:
                return ((OpaqueRenderFragmentPayload)reference.Payload!).Description.Scale.Resolve(
                    RecordedInputScales(reference, reference.Inputs.Length),
                    reference.RecordedBounds,
                    options.OutputScale,
                    options.MaxWorkingScale);
            case RenderFragmentKind.TargetCapture:
                {
                    TargetCaptureScaleContract scale =
                        ((TargetCaptureRenderFragmentPayload)reference.Payload!).Description.Scale;
                    return scale.PreservesTargetSupply
                        ? EffectiveScale.Unbounded
                        : scale.ResolveDeclared(
                            reference.RecordedBounds,
                            options.OutputScale,
                            options.MaxWorkingScale);
                }
            case RenderFragmentKind.TargetScope:
                return ((TargetScopeRenderFragmentPayload)reference.Payload!).Description.Scale.Resolve(
                    RecordedInputScales(reference, reference.Inputs.Length),
                    reference.RecordedBounds,
                    options.OutputScale,
                    options.MaxWorkingScale);
            case RenderFragmentKind.RawTargetScope:
                return ((RawTargetScopeRenderFragmentPayload)reference.Payload!).Description.Scale.Resolve(
                    RecordedInputScales(reference, reference.Inputs.Length),
                    reference.RecordedBounds,
                    options.OutputScale,
                    options.MaxWorkingScale);
            default:
                return null;
        }
    }

    private static EffectiveScale? ReplayWorkingScalePolicy(
        RenderFragmentReference reference,
        FilterEffectWorkingScalePolicy? policy,
        RenderRequestOptions options)
        => policy is { } declared
            ? declared.Resolve(
                RecordedInputScales(reference, reference.Inputs.Length),
                RecordedInputBounds(reference, reference.Inputs.Length),
                reference.RecordedBounds,
                options.OutputScale,
                options.MaxWorkingScale)
            : null;

    private static EffectiveScale? ReplayEffectItemScale(
        RenderFragmentReference reference,
        RenderRequestOptions options)
    {
        var payload = (FilterEffectSegmentRenderFragmentPayload)reference.Payload!;
        if (payload.WorkingScalePolicy is not { } policy)
            return null;

        Rect[] inputBounds = RecordedInputBounds(reference, payload.StreamInputCount);
        return policy.Resolve(
            RecordedInputScales(reference, payload.StreamInputCount),
            inputBounds,
            FilterEffectWorkingScalePolicy.CalculateEffectItemBufferBounds(
                inputBounds,
                payload.BoundsItems,
                reference.RecordedBounds),
            options.OutputScale,
            options.MaxWorkingScale);
    }

    private static Rect RecordedBoundsOf(RenderFragmentReference reference, int index)
        => reference.Inputs[index].RecordedBounds;

    private static Rect[] RecordedInputBounds(RenderFragmentReference reference, int count)
    {
        var bounds = new Rect[count];
        for (int index = 0; index < count; index++)
            bounds[index] = reference.Inputs[index].RecordedBounds;
        return bounds;
    }

    private static EffectiveScale[] RecordedInputScales(RenderFragmentReference reference, int count)
    {
        var scales = new EffectiveScale[count];
        for (int index = 0; index < count; index++)
            scales[index] = reference.Inputs[index].RecordedEffectiveScale;
        return scales;
    }

    private static string Describe(EffectiveScale scale)
        => scale.IsUnbounded ? "unbounded" : scale.Value.ToString("R", CultureInfo.InvariantCulture);
}
