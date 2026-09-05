using System.Globalization;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering.Requests;

/// <summary>
/// Fails a request whose symbolic fragment carries a metadata callback that no longer answers what it
/// answered while recording.
/// </summary>
/// <remarks>
/// <para>
/// A metadata callback must be deterministic over its inputs. <see cref="RegionAnalyzer"/> already holds a
/// concrete fragment to that, and it gets the check for free: the resolved answer is computed over the same
/// input bounds recording passed the callback, so requiring it to equal
/// <see cref="RenderFragmentReference.RecordedBounds"/> compares two evaluations of one callback at one
/// point. A symbolic fragment resolves over inputs that were meant to move, so its resolved answer is
/// expected to differ from the recorded one and that comparison has nothing to hold on to.
/// </para>
/// <para>
/// The recorded point is still comparable. Each callback is evaluated again over the inputs' recorded
/// metadata - the arguments recording actually passed it - and required to return what recording stored.
/// That is the same rule the concrete path enforces, moved to the one input where a recorded answer exists,
/// and it is the only form that catches the hazard: a value a callback reads that moved once, between
/// recording and resolution, and then stayed put reads identically on two evaluations at the resolved point,
/// so evaluating twice there would let it through.
/// </para>
/// <para>
/// Only author-supplied mappings are replayed. A fragment kind whose forward bounds or density is an engine
/// rule over its inputs - a passthrough, a union, a working-scale resolution - has no callback that could
/// answer differently, and replaying it would compare the engine against itself over inputs that legitimately
/// moved.
/// </para>
/// <para>
/// The hit-test contract is not covered because resolution does not evaluate it. Lowering a symbolic
/// fragment's rule re-reads an immutable property of the description the payload already holds, so there is no
/// second answer that could disagree with the first; the contract's own predicate runs when a point is tested,
/// after planning, where no recorded answer exists to hold it to.
/// </para>
/// <para>
/// Neither is the backward direction - <see cref="RenderBoundsContract.GetRequiredInputBounds"/>,
/// <see cref="OpaqueRenderBoundsContract"/>'s backward mapping, and
/// <see cref="RenderInputDemandContract"/>. Recording never evaluates those, so no recorded answer exists to
/// hold them to either, and the only mechanism left is evaluating one of them twice in a row, which catches a
/// callback that moves mid-resolution and not the one that moved before it. They are also unguarded for a
/// concrete fragment, so they are not the asymmetry this closes.
/// </para>
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

    private static Rect? ReplayRecordedBounds(RenderFragmentReference reference)
    {
        // An owning-domain fragment states no bounds of its own while recording, so the rectangle it carries
        // is a placeholder the domain replaces rather than an answer any mapping gave.
        if (reference.BoundsRequirement == RenderFragmentBoundsRequirement.OwningTargetDomain)
            return null;

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
