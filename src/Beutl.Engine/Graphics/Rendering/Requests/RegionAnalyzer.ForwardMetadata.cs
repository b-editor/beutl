using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RegionAnalyzer
{
    private static IReadOnlyDictionary<RenderFragmentReference, Rect?> ResolveTargetDomains(
        IReadOnlyList<RenderFragmentReference> roots,
        Rect? rootDomain,
        TargetDependencyPlan targetDependencies,
        IReadOnlySet<RenderFragmentId> backingTargetBackdropCaptures)
    {
        var result = new Dictionary<RenderFragmentReference, Rect?>(
            ReferenceEqualityComparer.Instance);
        var visitedDomains = new HashSet<VisitedDomain>(VisitedDomainComparer.Instance);
        foreach (RenderFragmentReference root in roots)
            Visit(root, rootDomain, result, visitedDomains);

        IReadOnlyDictionary<RenderFragmentId, RenderFragmentReference> references = result.Keys
            .Where(static reference => reference.Id is not null)
            .ToDictionary(static reference => reference.Id!.Value);
        IReadOnlyDictionary<TargetScopeId, TargetScopePlan> scopes = targetDependencies.Scopes
            .ToDictionary(static scope => scope.Id);
        foreach (TargetDependencyStep capture in targetDependencies.Steps
                     .Where(static step => step.Kind == TargetDependencyKind.Capture))
        {
            RenderFragmentReference reference = references[capture.FragmentId];
            TargetScopePlan captureScope = scopes[capture.ScopeId];
            if (backingTargetBackdropCaptures.Contains(capture.FragmentId))
            {
                // A value replay map materializes its input before replaying it through a transform. A backdrop
                // reads the backing target outside that materialization boundary; resolving it inside the map
                // would inverse-rasterize the target only to transform it forward again during replay.
                TargetScopePlan current = captureScope;
                while (current.ParentId is { } parentId)
                {
                    RenderFragmentReference? owner = current.OwnerFragmentId is { } ownerId
                        ? references[ownerId]
                        : null;
                    if (owner?.Payload is LayerRenderFragmentPayload
                        or TargetLayerScopeRenderFragmentPayload)
                    {
                        break;
                    }

                    if (owner?.Payload is TargetScopeRenderFragmentPayload scope
                        && scope.Description.BuiltInBackdropCapturesBackingTarget)
                    {
                        captureScope = scopes[parentId];
                    }

                    current = scopes[parentId];
                }
            }

            result[reference] = captureScope.ResolvedDomain;
        }
        return result;

        static void Visit(
            RenderFragmentReference reference,
            Rect? domain,
            Dictionary<RenderFragmentReference, Rect?> result,
            HashSet<VisitedDomain> visitedDomains)
        {
            if (!visitedDomains.Add(new VisitedDomain(reference, domain)))
                return;

            if (result.TryGetValue(reference, out Rect? existing))
            {
                bool isReusableCapture = reference.Kind is RenderFragmentKind.TargetCapture
                    or RenderFragmentKind.BuiltInBackdropCapture;
                if (existing != domain
                    && (reference.BoundsRequirement == RenderFragmentBoundsRequirement.OwningTargetDomain
                        || (reference.HasTargetEffects && !reference.CanBeUsedAsValueInput))
                    && !isReusableCapture)
                {
                    throw new InvalidOperationException(
                        "A target-effect fragment cannot be lowered into two different owning target domains.");
                }
            }
            else
            {
                result.Add(reference, domain);
            }

            Rect? inputDomain = reference.Payload switch
            {
                TargetScopeRenderFragmentPayload scope when domain is { } finite
                    => scope.Description.Bounds.GetRequiredInputBounds(finite),
                RawTargetScopeRenderFragmentPayload scope when domain is { } finite
                    => scope.Description.Bounds.GetRequiredInputBounds(finite),
                LayerRenderFragmentPayload layer => layer.Domain ?? domain,
                TargetLayerScopeRenderFragmentPayload layer
                    => ResolveTargetRegion(layer.Region, domain),
                _ => domain,
            };
            foreach (RenderFragmentReference input in reference.Inputs)
                Visit(input, inputDomain, result, visitedDomains);
        }
    }

    private readonly record struct VisitedDomain(RenderFragmentReference Reference, Rect? Domain);

    /// <summary>
    /// Pairs a fragment with a domain the traversal already lowered it into. One set for the whole traversal
    /// replaces the per-fragment set the domain map used to hold.
    /// </summary>
    private sealed class VisitedDomainComparer : IEqualityComparer<VisitedDomain>
    {
        public static readonly VisitedDomainComparer Instance = new();

        public bool Equals(VisitedDomain x, VisitedDomain y)
            => ReferenceEquals(x.Reference, y.Reference) && Nullable.Equals(x.Domain, y.Domain);

        public int GetHashCode(VisitedDomain obj)
            => HashCode.Combine(RuntimeHelpers.GetHashCode(obj.Reference), obj.Domain);
    }

    private static ImmutableHashSet<RenderFragmentId> FindBackingTargetBackdropCaptures(
        ImmutableArray<RenderFragmentReference> topologicalOrder,
        TargetDependencyPlan targetDependencies)
    {
        IReadOnlyDictionary<RenderFragmentId, RenderFragmentReference> references = topologicalOrder
            .ToDictionary(GetId);
        IReadOnlyDictionary<TargetScopeId, TargetScopePlan> scopes = targetDependencies.Scopes
            .ToDictionary(static scope => scope.Id);
        var result = ImmutableHashSet.CreateBuilder<RenderFragmentId>();
        foreach (TargetDependencyStep capture in targetDependencies.Steps
                     .Where(static step => step.Kind == TargetDependencyKind.Capture))
        {
            if (references[capture.FragmentId].Kind != RenderFragmentKind.BuiltInBackdropCapture)
                continue;

            TargetScopePlan current = scopes[capture.ScopeId];
            while (current.ParentId is { } parentId)
            {
                RenderFragmentReference? owner = current.OwnerFragmentId is { } ownerId
                    ? references[ownerId]
                    : null;
                if (owner?.Payload is LayerRenderFragmentPayload
                    or TargetLayerScopeRenderFragmentPayload)
                {
                    break;
                }

                if (owner?.Payload is TargetScopeRenderFragmentPayload scope
                    && scope.Description.BuiltInBackdropCapturesBackingTarget)
                {
                    result.Add(capture.FragmentId);
                    break;
                }

                current = scopes[parentId];
            }
        }

        return result.ToImmutable();
    }

    private static ImmutableDictionary<RenderFragmentId, ResolvedFragmentMetadata> ResolveForwardMetadata(
        ImmutableArray<RenderFragmentReference> topologicalOrder,
        IReadOnlyDictionary<RenderFragmentReference, Rect?> targetDomains,
        RenderRequestOptions options)
    {
        var result = ImmutableDictionary.CreateBuilder<RenderFragmentId, ResolvedFragmentMetadata>();
        foreach (RenderFragmentReference reference in topologicalOrder)
        {
            Rect resolvedBounds = ResolveForwardBounds(reference, targetDomains[reference]);
            RenderRectValidation.ThrowIfInvalidResult(
                resolvedBounds,
                "A resolved fragment contains invalid forward bounds.");
            if (!reference.HasSymbolicBoundsDependency)
            {
                if (resolvedBounds != reference.RecordedBounds)
                {
                    // Concrete mappings are deliberately evaluated both while recording and here. Exact equality
                    // enforces the public contract that bounds delegates are deterministic over an immutable snapshot;
                    // a tolerance would hide mutable captures rather than accommodate numeric drift from identical inputs.
                    throw new InvalidOperationException(
                        "A forward bounds mapping changed between recording and graph-wide metadata resolution.");
                }
            }
            else
            {
                // A symbolic fragment resolves over inputs that were meant to move, so the answer above is
                // expected to differ from the recorded one and cannot stand in for this comparison. Replaying
                // the mapping over the inputs' recorded metadata puts it back under the rule the branch above
                // enforces, at the one input where a recorded answer exists.
                SymbolicMetadataCrossCheck.VerifyForwardBounds(reference);
            }

            EffectiveScale resolvedScale;
            if (reference.HasSymbolicBoundsDependency)
            {
                resolvedScale = ResolveForwardScale(reference, resolvedBounds, options);
                // The density contract is held to its recorded answer the same way, and for the same reason.
                SymbolicMetadataCrossCheck.VerifyForwardScale(reference, options);
            }
            else
            {
                resolvedScale = reference.RecordedEffectiveScale;
            }
            RenderFragmentHitTest? resolvedHitTest = reference.HasSymbolicBoundsDependency
                ? ResolveForwardHitTest(reference)
                : null;
            reference.ApplyResolvedMetadata(resolvedBounds, resolvedScale, resolvedHitTest);
            result.Add(
                GetId(reference),
                new ResolvedFragmentMetadata(
                    resolvedBounds,
                    ResolveQueryBounds(reference),
                    resolvedScale));
        }

        return result.ToImmutable();
    }

    private static Rect ResolveForwardBounds(
        RenderFragmentReference reference,
        Rect? targetDomain)
    {
        if (reference.BoundsRequirement == RenderFragmentBoundsRequirement.OwningTargetDomain)
        {
            return targetDomain
                ?? throw new RenderTargetDomainRequiredException(reference.Kind == RenderFragmentKind.FilterEffectSegment
                    ? "A CustomEffect without transformBounds requires a finite owning target domain from a "
                      + "destination, finite Layer, or explicit TargetDomain."
                    : "A symbolic full-target capture requires a finite owning target domain.");
        }

        // Only the two arms below read more than the first input's bounds, so the projection array is built
        // there rather than for every fragment: this runs once per fragment in each of two metadata passes.
        switch (reference.Kind)
        {
            case RenderFragmentKind.OpaqueSource:
            case RenderFragmentKind.OpaqueMap:
            case RenderFragmentKind.OpaqueCombine:
            case RenderFragmentKind.OpaqueExpand:
                return ((OpaqueRenderFragmentPayload)reference.Payload!).Description.Bounds
                    .TransformBounds(reference.Inputs.SelectToArray(static input => input.Bounds));
            case RenderFragmentKind.FilterEffectSegment:
                return ResolveEffectItemBounds(reference);
        }

        return reference.Kind switch
        {
            RenderFragmentKind.ContributeValues when reference.Inputs.Length == 0
                => reference.RecordedBounds,
            RenderFragmentKind.ContributeValues
                or RenderFragmentKind.Opacity
                or RenderFragmentKind.Blend
                => reference.Inputs[0].Bounds,
            RenderFragmentKind.OpacityMask => reference.Inputs[0].Bounds,
            RenderFragmentKind.Shader
                => ((ShaderRenderFragmentPayload)reference.Payload!).Description.Bounds
                    .TransformBounds(reference.Inputs[0].Bounds),
            RenderFragmentKind.Geometry
                => ((GeometryRenderFragmentPayload)reference.Payload!).Description.Bounds
                    .TransformBounds(reference.Inputs[0].Bounds),
            RenderFragmentKind.Layer
                => ResolveLayerBounds(
                    reference,
                    ((LayerRenderFragmentPayload)reference.Payload!).Domain
                    ?? throw new InvalidOperationException(
                        "An owning-domain Layer must resolve before finite bounds mapping.")),
            RenderFragmentKind.TargetLayerScope => UnionInputBounds(reference),
            RenderFragmentKind.TargetScope
                => ((TargetScopeRenderFragmentPayload)reference.Payload!).Description.Bounds
                    .TransformBounds(reference.Inputs[0].Bounds),
            RenderFragmentKind.RawTargetScope
                => ((RawTargetScopeRenderFragmentPayload)reference.Payload!).Description.Bounds
                    .TransformBounds(reference.Inputs[0].Bounds),
            _ => reference.RecordedBounds,
        };
    }

    private static Rect ResolveEffectItemBounds(RenderFragmentReference reference)
    {
        var payload = (FilterEffectSegmentRenderFragmentPayload)reference.Payload!;
        if (payload.BoundsItems.IsDefaultOrEmpty)
            return reference.RecordedBounds;

        Rect bounds = default;
        for (int index = 0; index < payload.StreamInputCount; index++)
            bounds = bounds.Union(reference.Inputs[index].Bounds);
        foreach (IFEItem item in payload.BoundsItems)
            bounds = item.TransformBounds(bounds);
        return bounds;
    }

    private static EffectiveScale ResolveForwardScale(
        RenderFragmentReference reference,
        Rect resolvedBounds,
        RenderRequestOptions options)
    {
        // The projection array is built only in the arms that read more than the first input's scale, because
        // this runs once per fragment in each of two metadata passes.
        switch (reference.Kind)
        {
            case RenderFragmentKind.ContributeValues:
            case RenderFragmentKind.Opacity:
            case RenderFragmentKind.Blend:
            case RenderFragmentKind.OpacityMask:
                return reference.Inputs[0].EffectiveScale;
            case RenderFragmentKind.Shader:
                {
                    var payload = (ShaderRenderFragmentPayload)reference.Payload!;
                    bool materializes = payload.Description.Kind == ShaderDescriptionKind.WholeSource;
                    if (payload.WorkingScalePolicy is { } policy)
                    {
                        return policy.Resolve(
                            reference.Inputs,
                            resolvedBounds,
                            options.OutputScale,
                            options.MaxWorkingScale);
                    }

                    if (!materializes)
                        return reference.Inputs[0].EffectiveScale;

                    return ResolveMaterializedScale(InputScales(reference), resolvedBounds, options);
                }
            case RenderFragmentKind.Geometry:
                {
                    var payload = (GeometryRenderFragmentPayload)reference.Payload!;
                    return payload.WorkingScalePolicy is { } policy
                        ? policy.Resolve(
                            reference.Inputs,
                            resolvedBounds,
                            options.OutputScale,
                            options.MaxWorkingScale)
                        : ResolveMaterializedScale(InputScales(reference), resolvedBounds, options);
                }
            case RenderFragmentKind.FilterEffectSegment:
                {
                    var payload = (FilterEffectSegmentRenderFragmentPayload)reference.Payload!;
                    Rect[] inputBounds = reference.Inputs
                        .SelectToArray(payload.StreamInputCount, static input => input.Bounds);
                    EffectiveScale[] streamScales = reference.Inputs
                        .SelectToArray(payload.StreamInputCount, static input => input.EffectiveScale);
                    Rect[] bufferBounds = FilterEffectWorkingScalePolicy.CalculateEffectItemBufferBounds(
                        inputBounds,
                        payload.BoundsItems,
                        resolvedBounds);
                    return payload.WorkingScalePolicy is { } policy
                        ? policy.Resolve(
                            streamScales,
                            inputBounds,
                            bufferBounds,
                            options.OutputScale,
                            options.MaxWorkingScale)
                        : FilterEffectWorkingScalePolicy.ResolveMaterialized(
                            streamScales,
                            bufferBounds,
                            options.OutputScale,
                            options.MaxWorkingScale);
                }
            case RenderFragmentKind.OpaqueSource:
            case RenderFragmentKind.OpaqueMap:
            case RenderFragmentKind.OpaqueCombine:
            case RenderFragmentKind.OpaqueExpand:
                return ((OpaqueRenderFragmentPayload)reference.Payload!).Description.Scale.Resolve(
                    InputScales(reference),
                    resolvedBounds,
                    options.OutputScale,
                    options.MaxWorkingScale);
            case RenderFragmentKind.TargetCapture:
                {
                    TargetCaptureScaleContract scale =
                        ((TargetCaptureRenderFragmentPayload)reference.Payload!).Description.Scale;
                    return scale.PreservesTargetSupply
                        ? EffectiveScale.Unbounded
                        : scale.ResolveDeclared(
                            resolvedBounds,
                            options.OutputScale,
                            options.MaxWorkingScale);
                }
            case RenderFragmentKind.TargetScope:
                return ((TargetScopeRenderFragmentPayload)reference.Payload!).Description.Scale.Resolve(
                    InputScales(reference),
                    resolvedBounds,
                    options.OutputScale,
                    options.MaxWorkingScale);
            case RenderFragmentKind.RawTargetScope:
                return ((RawTargetScopeRenderFragmentPayload)reference.Payload!).Description.Scale.Resolve(
                    InputScales(reference),
                    resolvedBounds,
                    options.OutputScale,
                    options.MaxWorkingScale);
            default:
                return reference.RecordedEffectiveScale;
        }
    }

    private static EffectiveScale[] InputScales(RenderFragmentReference reference)
        => reference.Inputs.SelectToArray(static input => input.EffectiveScale);

    /// <summary>
    /// The rule a symbolic fragment answers with once its own bounds and its inputs' are known.
    /// </summary>
    /// <remarks>
    /// A fragment records the best rule the recording could state. Where that rule stood in for information
    /// only graph-wide resolution has - a domain that was not yet finite, an input whose extent was symbolic -
    /// this replaces it with the one the resolved graph supports.
    /// </remarks>
    private static RenderFragmentHitTest ResolveForwardHitTest(RenderFragmentReference reference)
    {
        return reference.Kind switch
        {
            RenderFragmentKind.ContributeValues
                or RenderFragmentKind.Opacity
                or RenderFragmentKind.Blend
                or RenderFragmentKind.OpacityMask
                or RenderFragmentKind.Layer
                or RenderFragmentKind.TargetLayerScope
                => RenderFragmentHitTest.Inputs,
            RenderFragmentKind.Shader
                => ((ShaderRenderFragmentPayload)reference.Payload!).Description.CreateFragmentHitTest(),
            RenderFragmentKind.Geometry
                => FromDescription(((GeometryRenderFragmentPayload)reference.Payload!).Description),
            RenderFragmentKind.OpaqueSource
                or RenderFragmentKind.OpaqueMap
                or RenderFragmentKind.OpaqueCombine
                or RenderFragmentKind.OpaqueExpand
                => FromDescription(((OpaqueRenderFragmentPayload)reference.Payload!).Description),
            RenderFragmentKind.FilterEffectSegment => RenderFragmentHitTest.Bounds,
            RenderFragmentKind.MaterializedInput
                => RenderFragmentHitTest.FromContract(
                    ((MaterializedInputRenderFragmentPayload)reference.Payload!).Description.HitTest,
                    ((MaterializedInputRenderFragmentPayload)reference.Payload!).Description.Resources),
            RenderFragmentKind.TargetCapture
                => RenderFragmentHitTest.FromContract(
                    ((TargetCaptureRenderFragmentPayload)reference.Payload!).Description.HitTest,
                    ((TargetCaptureRenderFragmentPayload)reference.Payload!).Description.Resources),
            RenderFragmentKind.BuiltInBackdropCapture
                => RenderFragmentHitTest.FromContract(
                    ((BuiltInBackdropCaptureRenderFragmentPayload)reference.Payload!).Description.HitTest,
                    ((BuiltInBackdropCaptureRenderFragmentPayload)reference.Payload!).Description.Resources),
            RenderFragmentKind.TargetScope
                => FromDescription(((TargetScopeRenderFragmentPayload)reference.Payload!).Description),
            RenderFragmentKind.RawTargetScope
                => FromDescription(((RawTargetScopeRenderFragmentPayload)reference.Payload!).Description),
            RenderFragmentKind.RawTargetCommand
                => FromDescription(((RawTargetCommandRenderFragmentPayload)reference.Payload!).Description),
            RenderFragmentKind.TargetCommand
                => FromDescription(((TargetCommandRenderFragmentPayload)reference.Payload!).Description),
            _ => throw new InvalidOperationException(
                $"Fragment kind '{reference.Kind}' has no symbolic hit-test lowering rule."),
        };
    }

    private static RenderFragmentHitTest FromDescription(GeometryDescription description)
        => RenderFragmentHitTest.FromContract(description.HitTest, description.Resources);

    private static RenderFragmentHitTest FromDescription(OpaqueRenderDescription description)
        => RenderFragmentHitTest.FromContract(description.HitTest, description.Resources);

    private static RenderFragmentHitTest FromDescription(TargetScopeDescription description)
        => RenderFragmentHitTest.FromContract(description.HitTest, description.Resources);

    private static RenderFragmentHitTest FromDescription(RawTargetScopeDescription description)
        => RenderFragmentHitTest.FromContract(description.HitTest, description.Resources);

    private static RenderFragmentHitTest FromDescription(RawTargetCommandDescription description)
        => RenderFragmentHitTest.FromContract(description.HitTest, description.Resources);

    private static RenderFragmentHitTest FromDescription(TargetCommandDescription description)
        => RenderFragmentHitTest.FromContract(description.HitTest, description.Resources);

    private static EffectiveScale ResolveMaterializedScale(
        EffectiveScale[] inputScales,
        Rect resolvedBounds,
        RenderRequestOptions options)
    {
        float workingScale = RenderScaleUtilities.ResolveWorkingScale(
            inputScales,
            options.OutputScale,
            options.MaxWorkingScale);
        workingScale = BufferDimensionBudget.EngineCeiling.ClampWorkingScaleToExactFootprint(
            resolvedBounds,
            workingScale);
        return EffectiveScale.At(workingScale);
    }

    private static Rect ResolveLayerBounds(
        RenderFragmentReference reference,
        Rect domain)
    {
        Rect bounds = default;
        foreach (RenderFragmentReference input in reference.Inputs)
        {
            if (input.ContributesValuesToTarget)
                bounds = bounds.Union(input.Bounds);
            if (TargetWriteMetadataResolver.Resolve(input, domain) is { } affected)
                bounds = bounds.Union(affected);
        }

        return bounds.Intersect(domain);
    }

    private static Rect UnionInputBounds(RenderFragmentReference reference)
    {
        Rect bounds = default;
        foreach (RenderFragmentReference input in reference.Inputs)
            bounds = bounds.Union(input.Bounds);
        return bounds;
    }
}
