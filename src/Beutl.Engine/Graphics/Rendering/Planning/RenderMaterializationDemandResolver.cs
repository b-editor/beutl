using Beutl.Graphics.Effects;
using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Rendering;

internal static class RenderMaterializationDemandResolver
{
    private enum DemandUse : byte
    {
        ReplayTarget,
        MaterializeValue,
    }

    private readonly record struct PendingDemand(
        RenderFragmentReference Fragment,
        float Demand,
        DemandUse Use,
        bool UseSupplyFallback,
        bool? IsEffectClassConsumer);

    public static RenderMaterializationDemandResolution Resolve(
        IReadOnlyList<RenderFragmentReference> roots,
        float outputScale,
        float maxWorkingScale,
        IReadOnlySet<RenderFragmentReference>? cacheBoundaries = null)
    {
        ArgumentNullException.ThrowIfNull(roots);
        if (!float.IsFinite(outputScale) || outputScale <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outputScale),
                outputScale,
                "The output density must be finite and positive.");
        }

        var result = new Dictionary<RenderFragmentReference, EffectiveScale>(
            ReferenceEqualityComparer.Instance);
        var replayDemands = new Dictionary<RenderFragmentReference, float>(
            ReferenceEqualityComparer.Instance);
        var materializedDemands = new Dictionary<RenderFragmentReference, float>(
            ReferenceEqualityComparer.Instance);
        var materializedUses = new HashSet<RenderFragmentReference>(
            ReferenceEqualityComparer.Instance);
        var effectClassUses = new HashSet<RenderFragmentReference>(
            ReferenceEqualityComparer.Instance);
        var otherUses = new HashSet<RenderFragmentReference>(
            ReferenceEqualityComparer.Instance);
        var pending = new Stack<PendingDemand>();
        float rootDemand = MathF.Min(
            outputScale,
            RenderScaleUtilities.SanitizeMaxWorkingScale(maxWorkingScale));
        for (int index = roots.Count - 1; index >= 0; index--)
        {
            pending.Push(new PendingDemand(
                roots[index],
                rootDemand,
                DemandUse.ReplayTarget,
                UseSupplyFallback: false,
                IsEffectClassConsumer: null));
        }

        while (pending.TryPop(out var item))
        {
            RenderFragmentReference fragment = item.Fragment;
            if (item.Use == DemandUse.ReplayTarget
                && cacheBoundaries?.Contains(fragment) == true)
            {
                pending.Push(new PendingDemand(
                    fragment,
                    item.Demand,
                    DemandUse.MaterializeValue,
                    item.UseSupplyFallback,
                    IsEffectClassConsumer: false));
                continue;
            }

            float demand = ResolveDemand(
                fragment,
                item.Demand,
                item.UseSupplyFallback,
                maxWorkingScale);
            bool outputDemandChanged = MergeDemand(result, fragment, demand);
            if (outputDemandChanged && materializedUses.Contains(fragment))
            {
                pending.Push(new PendingDemand(
                    fragment,
                    demand,
                    DemandUse.MaterializeValue,
                    UseSupplyFallback: false,
                    IsEffectClassConsumer: null));
            }

            if (item.Use == DemandUse.MaterializeValue)
            {
                materializedUses.Add(fragment);
                if (item.IsEffectClassConsumer is true)
                    effectClassUses.Add(fragment);
                else if (item.IsEffectClassConsumer is false)
                    otherUses.Add(fragment);
                float selectedDemand = result[fragment].Value;
                if (!MergeProcessedDemand(materializedDemands, fragment, selectedDemand))
                    continue;

                EnqueueMaterializedInputs(
                    fragment,
                    selectedDemand,
                    maxWorkingScale,
                    pending);
                continue;
            }

            if (!MergeProcessedDemand(replayDemands, fragment, item.Demand))
                continue;

            EnqueueReplayInputs(fragment, item.Demand, maxWorkingScale, pending);
        }

        effectClassUses.ExceptWith(otherUses);
        return new RenderMaterializationDemandResolution(
            result,
            materializedUses,
            effectClassUses);
    }

    private static float ResolveDemand(
        RenderFragmentReference fragment,
        float requestedDemand,
        bool useSupplyFallback,
        float maxWorkingScale)
    {
        if (!fragment.EffectiveScale.IsUnbounded)
            return fragment.EffectiveScale.Value;

        float demand = requestedDemand;
        // A target command does not provide a caller density. Preserve the effect-item
        // Layer contract by negotiating from its densest concrete child supply.
        if (useSupplyFallback && fragment.Kind == RenderFragmentKind.Layer)
        {
            foreach (RenderFragmentReference input in fragment.Inputs)
            {
                if (!input.EffectiveScale.IsUnbounded)
                    demand = MathF.Max(demand, input.EffectiveScale.Value);
            }
        }

        demand = MathF.Min(
            demand,
            RenderScaleUtilities.SanitizeMaxWorkingScale(maxWorkingScale));
        return RenderMaterializationDensityPolicy.Clamp(
            fragment,
            demand);
    }

    private static bool MergeDemand(
        IDictionary<RenderFragmentReference, EffectiveScale> demands,
        RenderFragmentReference fragment,
        float demand)
    {
        if (demands.TryGetValue(fragment, out EffectiveScale existing)
            && existing.Value >= demand)
        {
            return false;
        }

        demands[fragment] = EffectiveScale.At(demand);
        return true;
    }

    private static bool MergeProcessedDemand(
        IDictionary<RenderFragmentReference, float> demands,
        RenderFragmentReference fragment,
        float demand)
    {
        if (demands.TryGetValue(fragment, out float existing) && existing >= demand)
            return false;

        demands[fragment] = demand;
        return true;
    }

    private static void EnqueueReplayInputs(
        RenderFragmentReference fragment,
        float targetDemand,
        float maxWorkingScale,
        Stack<PendingDemand> pending)
    {
        switch (fragment.Kind)
        {
            case RenderFragmentKind.Opacity:
            case RenderFragmentKind.Blend:
            case RenderFragmentKind.TargetLayerScope:
                EnqueueInputs(fragment, targetDemand, DemandUse.ReplayTarget, pending);
                return;
            case RenderFragmentKind.RawTargetScope:
                // A raw scope hands an unguarded canvas to an opaque callback, so its declared scale
                // contract is the only thing that says how the replayed input is consumed. The backward
                // half is identity unless the author asked for MapInputSupply, so a scope that carries its
                // enlargement in the destination matrix stays unchanged, and one that resamples its input
                // gets the density it declared instead of rasterizing at the target's and stretching.
                float rawScopeInputDemand =
                    fragment.Payload is RawTargetScopeRenderFragmentPayload rawScopePayload
                        ? ResolveMappedInputDemand(
                            rawScopePayload.Description.Scale,
                            targetDemand,
                            maxWorkingScale)
                        : targetDemand;
                EnqueueInputs(fragment, rawScopeInputDemand, DemandUse.ReplayTarget, pending);
                return;
            case RenderFragmentKind.TargetScope:
                TargetScopeDescription targetScope =
                    ((TargetScopeRenderFragmentPayload)fragment.Payload!).Description;
                // Only a scope that says its transform is in the input's own coordinates. One defined
                // against the ambient target transform - TransformOperator.Append - has that scale carried
                // by the destination already, so pre-scaling the input would rasterize it twice as large
                // and then draw it scaled again.
                float inputDemand = targetScope.TransformSpace == RenderScopeTransformSpace.InputLogical
                    ? ResolveMappedInputDemand(
                        targetScope.Scale,
                        targetDemand,
                        maxWorkingScale)
                    : targetDemand;
                EnqueueInputs(fragment, inputDemand, DemandUse.ReplayTarget, pending);
                return;
            case RenderFragmentKind.OpacityMask:
                if (fragment.Inputs.Length > 0)
                {
                    for (int index = fragment.Inputs.Length - 1; index >= 1; index--)
                    {
                        pending.Push(new PendingDemand(
                            fragment.Inputs[index],
                            targetDemand,
                            DemandUse.MaterializeValue,
                            UseSupplyFallback: false,
                            IsEffectClassConsumer: IsEffectClassConsumer(fragment)));
                    }

                    pending.Push(new PendingDemand(
                        fragment.Inputs[0],
                        targetDemand,
                        DemandUse.ReplayTarget,
                        UseSupplyFallback: false,
                        IsEffectClassConsumer: null));
                }
                return;
            case RenderFragmentKind.TargetCommand:
                RenderInputDemandContract commandDemand =
                    fragment.Payload is TargetCommandRenderFragmentPayload commandPayload
                        ? commandPayload.Description.InputDemand
                        : default;
                for (int index = fragment.Inputs.Length - 1; index >= 0; index--)
                {
                    pending.Push(new PendingDemand(
                        fragment.Inputs[index],
                        ResolveMappedInputDemand(
                            commandDemand,
                            index,
                            targetDemand,
                            maxWorkingScale),
                        DemandUse.MaterializeValue,
                        UseSupplyFallback: true,
                        IsEffectClassConsumer: IsEffectClassConsumer(fragment)));
                }
                return;
            case RenderFragmentKind.RawTargetCommand:
                return;
            case RenderFragmentKind.ContributeValues:
                EnqueueInputs(
                    fragment,
                    targetDemand,
                    DemandUse.MaterializeValue,
                    pending,
                    IsEffectClassConsumer(fragment));
                return;
            default:
                pending.Push(new PendingDemand(
                    fragment,
                    targetDemand,
                    DemandUse.MaterializeValue,
                    UseSupplyFallback: false,
                    IsEffectClassConsumer: false));
                return;
        }
    }

    private static void EnqueueMaterializedInputs(
        RenderFragmentReference fragment,
        float valueDemand,
        float maxWorkingScale,
        Stack<PendingDemand> pending)
    {
        switch (fragment.Kind)
        {
            case RenderFragmentKind.Layer:
                EnqueueInputs(fragment, valueDemand, DemandUse.ReplayTarget, pending);
                return;
            case RenderFragmentKind.TargetScope:
                TargetScopeDescription targetScope =
                    ((TargetScopeRenderFragmentPayload)fragment.Payload!).Description;
                float targetScopeInputDemand =
                    targetScope.TransformSpace == RenderScopeTransformSpace.InputLogical
                        ? ResolveMappedInputDemand(
                            targetScope.Scale,
                            valueDemand,
                            maxWorkingScale)
                        : valueDemand;
                EnqueueInputs(
                    fragment,
                    targetScopeInputDemand,
                    DemandUse.ReplayTarget,
                    pending);
                return;
            case RenderFragmentKind.OpaqueMap:
                OpaqueRenderDescription description =
                    ((OpaqueRenderFragmentPayload)fragment.Payload!).Description;
                float inputDemand = ResolveMappedInputDemand(
                    description.Scale,
                    valueDemand,
                    maxWorkingScale);
                EnqueueInputs(fragment, inputDemand, DemandUse.MaterializeValue, pending);
                return;
            case RenderFragmentKind.OpaqueCombine:
            case RenderFragmentKind.OpaqueExpand:
                OpaqueRenderDescription many =
                    ((OpaqueRenderFragmentPayload)fragment.Payload!).Description;
                if (many.InputDemand.IsUnchanged)
                {
                    EnqueueInputs(
                        fragment,
                        valueDemand,
                        DemandUse.MaterializeValue,
                        pending,
                        IsEffectClassConsumer(fragment));
                    return;
                }

                for (int index = fragment.Inputs.Length - 1; index >= 0; index--)
                {
                    pending.Push(new PendingDemand(
                        fragment.Inputs[index],
                        ResolveMappedInputDemand(many.InputDemand, index, valueDemand, maxWorkingScale),
                        DemandUse.MaterializeValue,
                        UseSupplyFallback: false,
                        IsEffectClassConsumer: IsEffectClassConsumer(fragment)));
                }
                return;
            case RenderFragmentKind.Shader:
                ShaderDescription shader =
                    ((ShaderRenderFragmentPayload)fragment.Payload!).Description;
                EnqueueInputs(
                    fragment,
                    ResolveMappedInputDemand(shader.InputDemand, 0, valueDemand, maxWorkingScale),
                    DemandUse.MaterializeValue,
                    pending,
                    IsEffectClassConsumer(fragment));
                return;
            case RenderFragmentKind.Geometry:
                GeometryDescription geometry =
                    ((GeometryRenderFragmentPayload)fragment.Payload!).Description;
                EnqueueInputs(
                    fragment,
                    ResolveMappedInputDemand(geometry.InputDemand, 0, valueDemand, maxWorkingScale),
                    DemandUse.MaterializeValue,
                    pending,
                    IsEffectClassConsumer(fragment));
                return;
            case RenderFragmentKind.MaterializedInput:
            case RenderFragmentKind.TargetCapture:
            case RenderFragmentKind.BuiltInBackdropCapture:
                return;
            default:
                EnqueueInputs(
                    fragment,
                    valueDemand,
                    DemandUse.MaterializeValue,
                    pending,
                    IsEffectClassConsumer(fragment));
                return;
        }
    }

    private static void EnqueueInputs(
        RenderFragmentReference fragment,
        float demand,
        DemandUse use,
        Stack<PendingDemand> pending,
        bool? isEffectClassConsumer = null)
    {
        for (int index = fragment.Inputs.Length - 1; index >= 0; index--)
        {
            pending.Push(new PendingDemand(
                fragment.Inputs[index],
                demand,
                use,
                UseSupplyFallback: false,
                IsEffectClassConsumer: isEffectClassConsumer));
        }
    }

    private static float ResolveMappedInputDemand(
        RenderInputDemandContract inputDemand,
        int inputIndex,
        float outputDemand,
        float maxWorkingScale)
        => BoundMappedInputDemand(
            inputDemand.Resolve(inputIndex, EffectiveScale.At(outputDemand)).Value,
            maxWorkingScale);

    private static float ResolveMappedInputDemand(
        RenderScaleContract scale,
        float outputDemand,
        float maxWorkingScale)
        => BoundMappedInputDemand(
            scale.MapOutputDemandToInput(EffectiveScale.At(outputDemand)).Value,
            maxWorkingScale);

    private static float BoundMappedInputDemand(float mapped, float maxWorkingScale)
    {
        // Cap amplification at the request ceiling before another map can observe it. The pending
        // input's ResolveDemand pass applies its own logical-bounds buffer budget if it materializes.
        return MathF.Min(
            mapped,
            RenderScaleUtilities.SanitizeMaxWorkingScale(maxWorkingScale));
    }

    private static bool IsEffectClassConsumer(RenderFragmentReference fragment)
        => fragment.Payload is FilterEffectSegmentRenderFragmentPayload
            or ShaderRenderFragmentPayload
            or GeometryRenderFragmentPayload;
}
