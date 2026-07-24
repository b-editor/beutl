using System.Collections.Immutable;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

internal sealed class RegionAnalyzer
{
    public RegionAnalysis Analyze(
        RenderRequestOptions options,
        IReadOnlyList<RenderFragmentReference> roots,
        TargetDependencyPlan? targetDependencies = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(roots);

        targetDependencies ??= TargetDependencyLowerer.Lower(
            roots.ToImmutableArray(),
            options.TargetDomain);

        ImmutableArray<RenderFragmentReference> topologicalOrder = GetTopologicalOrder(roots);
        IReadOnlyDictionary<RenderFragmentReference, Rect?> targetDomains =
            ResolveTargetDomains(roots, options.TargetDomain, targetDependencies);
        ImmutableDictionary<RenderFragmentId, ResolvedFragmentMetadata> metadata =
            ResolveForwardMetadata(topologicalOrder, targetDomains, options);
        RenderNodeMeasurement measurement = Measure(options, roots);
        Rect finalCommitBounds = options.RequestedRegion switch
        {
            { Width: 0 } empty => empty,
            { Height: 0 } empty => empty,
            { } requested => requested.Intersect(measurement.OutputBounds),
            null => measurement.OutputBounds,
        };
        RequiredRegion finalCommitRegion = RequiredRegion.Region(finalCommitBounds);

        var fragmentRequirements = new Dictionary<RenderFragmentReference, RequiredRegion>(
            ReferenceEqualityComparer.Instance);
        foreach (RenderFragmentReference root in roots)
        {
            RequiredRegion requirement = GetRootRequirement(
                root,
                finalCommitBounds,
                options.TargetDomain);
            UnionRequirement(fragmentRequirements, root, requirement);
        }

        var targetRequirements = new Dictionary<RenderFragmentReference, RequiredRegion>(
            ReferenceEqualityComparer.Instance);
        IReadOnlyDictionary<RenderFragmentId, RenderFragmentReference> referencesById =
            topologicalOrder.ToDictionary(GetId);
        int remainingPasses = checked(topologicalOrder.Length + targetDependencies.Steps.Length + 1);
        bool changed;
        do
        {
            changed = PropagateValueRequirements(
                topologicalOrder,
                targetDomains,
                fragmentRequirements,
                targetRequirements);
            changed |= PropagateTargetTokenRequirements(
                targetDependencies,
                referencesById,
                targetRequirements,
                fragmentRequirements);
            remainingPasses--;
            if (changed && remainingPasses == 0)
            {
                throw new InvalidOperationException(
                    "Target-token region propagation did not converge within the finite request graph.");
            }
        }
        while (changed);

        var fragmentRegions = ImmutableDictionary.CreateBuilder<RenderFragmentId, RequiredRegion>();
        var valueRegions = ImmutableDictionary.CreateBuilder<RenderValueId, RequiredRegion>();
        var targetAccessRegions = ImmutableDictionary.CreateBuilder<RenderFragmentId, RequiredRegion>();
        foreach (RenderFragmentReference reference in topologicalOrder)
        {
            RenderFragmentId fragmentId = GetId(reference);
            RequiredRegion requirement = GetRequirement(fragmentRequirements, reference);
            fragmentRegions.Add(fragmentId, requirement);
            foreach (RenderValueId valueId in reference.ValueIds)
                valueRegions.Add(valueId, requirement);

            if (targetRequirements.TryGetValue(reference, out RequiredRegion targetRequirement))
                targetAccessRegions.Add(fragmentId, targetRequirement);
        }

        return new RegionAnalysis(
            measurement,
            options.TargetDomain,
            options.RequestedRegion,
            finalCommitBounds,
            finalCommitRegion,
            fragmentRegions.ToImmutable(),
            valueRegions.ToImmutable(),
            targetAccessRegions.ToImmutable(),
            metadata);
    }

    private static bool PropagateValueRequirements(
        ImmutableArray<RenderFragmentReference> topologicalOrder,
        IReadOnlyDictionary<RenderFragmentReference, Rect?> targetDomains,
        Dictionary<RenderFragmentReference, RequiredRegion> fragmentRequirements,
        Dictionary<RenderFragmentReference, RequiredRegion> targetRequirements)
    {
        bool changed = false;
        for (int index = topologicalOrder.Length - 1; index >= 0; index--)
        {
            RenderFragmentReference reference = topologicalOrder[index];
            RequiredRegion requirement = GetRequirement(fragmentRequirements, reference);

            RequiredRegion? targetRequirement = GetTargetAccessRequirement(
                reference,
                requirement,
                targetDomains[reference]);
            if (targetRequirement is { } target)
                changed |= UnionRequirement(targetRequirements, reference, target);

            ImmutableArray<RequiredRegion> inputRequirements = GetInputRequirements(
                reference,
                requirement,
                targetDomains[reference]);
            if (inputRequirements.Length != reference.Inputs.Length)
            {
                throw new InvalidOperationException(
                    "Region analysis must produce exactly one requirement per fragment input.");
            }

            for (int inputIndex = 0; inputIndex < reference.Inputs.Length; inputIndex++)
            {
                changed |= UnionRequirement(
                    fragmentRequirements,
                    reference.Inputs[inputIndex],
                    inputRequirements[inputIndex]);
            }
        }

        return changed;
    }

    private static bool PropagateTargetTokenRequirements(
        TargetDependencyPlan plan,
        IReadOnlyDictionary<RenderFragmentId, RenderFragmentReference> referencesById,
        IReadOnlyDictionary<RenderFragmentReference, RequiredRegion> targetRequirements,
        Dictionary<RenderFragmentReference, RequiredRegion> fragmentRequirements)
    {
        IReadOnlyDictionary<TargetTokenId, TargetDependencyStep> producers = plan.Steps
            .ToDictionary(static step => step.OutputToken);
        IReadOnlyDictionary<TargetScopeId, TargetScopePlan> scopes = plan.Scopes
            .ToDictionary(static scope => scope.Id);
        bool changed = false;

        foreach (TargetDependencyStep consumer in plan.Steps)
        {
            RenderFragmentReference consumerReference = referencesById[consumer.FragmentId];
            if (!targetRequirements.TryGetValue(
                    consumerReference,
                    out RequiredRegion targetRequirement)
                || targetRequirement.IsEmpty)
            {
                continue;
            }

            TargetTokenId token = consumer.InputToken;
            TargetScopeId requirementScope = consumer.ScopeId;
            RequiredRegion requirement = targetRequirement;
            while (producers.TryGetValue(token, out TargetDependencyStep producer))
            {
                RenderFragmentReference producerReference = referencesById[producer.FragmentId];
                TargetScopeId fragmentScope = ResolveFragmentOutputScope(
                    producerReference,
                    producer.ScopeId,
                    scopes);
                RequiredRegion fragmentRequirement = MapRequirementBetweenScopes(
                    requirement,
                    requirementScope,
                    fragmentScope,
                    scopes,
                    referencesById);

                if (producer.Kind != TargetDependencyKind.Capture)
                {
                    changed |= UnionRequirement(
                        fragmentRequirements,
                        producerReference,
                        fragmentRequirement);
                }

                requirement = MapRequirementBetweenScopes(
                    requirement,
                    requirementScope,
                    producer.ScopeId,
                    scopes,
                    referencesById);
                requirementScope = producer.ScopeId;
                token = producer.InputToken;
            }
        }

        return changed;
    }

    private static TargetScopeId ResolveFragmentOutputScope(
        RenderFragmentReference reference,
        TargetScopeId executionScope,
        IReadOnlyDictionary<TargetScopeId, TargetScopePlan> scopes)
    {
        TargetScopePlan scope = scopes[executionScope];
        return scope.OwnerFragmentId == reference.Id && scope.ParentId is { } parentId
            ? parentId
            : executionScope;
    }

    private static RequiredRegion MapRequirementBetweenScopes(
        RequiredRegion requirement,
        TargetScopeId sourceScopeId,
        TargetScopeId destinationScopeId,
        IReadOnlyDictionary<TargetScopeId, TargetScopePlan> scopes,
        IReadOnlyDictionary<RenderFragmentId, RenderFragmentReference> referencesById)
    {
        if (requirement.IsEmpty || sourceScopeId == destinationScopeId)
            return requirement;

        TargetScopePlan sourceScope = scopes[sourceScopeId];
        Rect sourceDomain = sourceScope.ResolvedDomain
            ?? throw new InvalidOperationException(
                "A target-token requirement cannot cross an unresolved target scope.");
        Rect mapped = requirement.Resolve(sourceDomain);

        var sourceAncestors = new Dictionary<TargetScopeId, int>();
        TargetScopeId? cursor = sourceScopeId;
        int depth = 0;
        while (cursor is { } current)
        {
            sourceAncestors.Add(current, depth++);
            cursor = scopes[current].ParentId;
        }

        var destinationPath = new List<TargetScopeId>();
        cursor = destinationScopeId;
        while (cursor is { } current && !sourceAncestors.ContainsKey(current))
        {
            destinationPath.Add(current);
            cursor = scopes[current].ParentId;
        }

        TargetScopeId commonAncestor = cursor
            ?? throw new InvalidOperationException(
                "Target-token scopes must belong to one rooted scope tree.");
        cursor = sourceScopeId;
        while (cursor != commonAncestor)
        {
            TargetScopePlan child = scopes[cursor!.Value];
            mapped = MapChildToParent(mapped, child, referencesById);
            cursor = child.ParentId;
        }

        for (int index = destinationPath.Count - 1; index >= 0; index--)
        {
            TargetScopePlan child = scopes[destinationPath[index]];
            mapped = MapParentToChild(mapped, child, referencesById);
        }

        return RequiredRegion.Region(mapped);
    }

    private static Rect MapChildToParent(
        Rect requirement,
        TargetScopePlan child,
        IReadOnlyDictionary<RenderFragmentId, RenderFragmentReference> referencesById)
    {
        Rect mapped = child.OwnerFragmentId is { } ownerId
            ? referencesById[ownerId].Payload switch
            {
                TargetScopeRenderFragmentPayload scope
                    => scope.Description.Bounds.TransformBounds(requirement),
                RawTargetScopeRenderFragmentPayload scope
                    => scope.Description.Bounds.TransformBounds(requirement),
                _ => requirement,
            }
            : requirement;
        return mapped;
    }

    private static Rect MapParentToChild(
        Rect requirement,
        TargetScopePlan child,
        IReadOnlyDictionary<RenderFragmentId, RenderFragmentReference> referencesById)
    {
        Rect mapped = child.OwnerFragmentId is { } ownerId
            ? referencesById[ownerId].Payload switch
            {
                TargetScopeRenderFragmentPayload scope
                    => scope.Description.Bounds.GetRequiredInputBounds(requirement),
                RawTargetScopeRenderFragmentPayload scope
                    => scope.Description.Bounds.GetRequiredInputBounds(requirement),
                _ => requirement,
            }
            : requirement;
        return child.ResolvedDomain is { } domain
            ? mapped.Intersect(domain)
            : mapped;
    }

    private static ImmutableArray<RenderFragmentReference> GetTopologicalOrder(
        IReadOnlyList<RenderFragmentReference> roots)
    {
        var result = ImmutableArray.CreateBuilder<RenderFragmentReference>();
        var visiting = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
        var visited = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
        foreach (RenderFragmentReference root in roots)
        {
            ArgumentNullException.ThrowIfNull(root);
            Visit(root, visiting, visited, result);
        }

        return result.ToImmutable();

        static void Visit(
            RenderFragmentReference reference,
            HashSet<RenderFragmentReference> visiting,
            HashSet<RenderFragmentReference> visited,
            ImmutableArray<RenderFragmentReference>.Builder result)
        {
            if (visited.Contains(reference))
                return;
            if (!visiting.Add(reference))
                throw new InvalidOperationException("The recorded render graph contains a fragment cycle.");

            foreach (RenderFragmentReference input in reference.Inputs)
                Visit(input, visiting, visited, result);

            visiting.Remove(reference);
            visited.Add(reference);
            result.Add(reference);
        }
    }

    private static IReadOnlyDictionary<RenderFragmentReference, Rect?> ResolveTargetDomains(
        IReadOnlyList<RenderFragmentReference> roots,
        Rect? rootDomain,
        TargetDependencyPlan targetDependencies)
    {
        var result = new Dictionary<RenderFragmentReference, Rect?>(
            ReferenceEqualityComparer.Instance);
        var visitedDomains = new Dictionary<RenderFragmentReference, HashSet<Rect?>>(
            ReferenceEqualityComparer.Instance);
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
            result[reference] = scopes[capture.ScopeId].ResolvedDomain;
        }
        return result;

        static void Visit(
            RenderFragmentReference reference,
            Rect? domain,
            Dictionary<RenderFragmentReference, Rect?> result,
            Dictionary<RenderFragmentReference, HashSet<Rect?>> visitedDomains)
        {
            if (!visitedDomains.TryGetValue(reference, out HashSet<Rect?>? domains))
            {
                domains = [];
                visitedDomains.Add(reference, domains);
            }
            if (!domains.Add(domain))
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
            if (!reference.HasSymbolicBoundsDependency
                && resolvedBounds != reference.RecordedBounds)
            {
                // Concrete mappings are deliberately evaluated both while recording and here. Exact equality
                // enforces the public contract that bounds delegates are deterministic over an immutable snapshot;
                // a tolerance would hide mutable captures rather than accommodate numeric drift from identical inputs.
                throw new InvalidOperationException(
                    "A forward bounds mapping changed between recording and graph-wide metadata resolution.");
            }

            EffectiveScale resolvedScale = reference.HasSymbolicBoundsDependency
                ? ResolveForwardScale(reference, resolvedBounds, options)
                : reference.RecordedEffectiveScale;
            Func<Point, bool>? resolvedHitTest = reference.HasSymbolicBoundsDependency
                ? ResolveForwardHitTest(reference, resolvedBounds)
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
                ?? throw new InvalidOperationException(reference.Kind == RenderFragmentKind.LegacyFilterEffect
                    ? "A CustomEffect without transformBounds requires a finite owning target domain from a "
                      + "destination, finite Layer, or explicit TargetDomain."
                    : "A symbolic full-target capture requires a finite owning target domain.");
        }

        IReadOnlyList<Rect> inputBounds = reference.Inputs
            .Select(static input => input.Bounds)
            .ToArray();
        return reference.Kind switch
        {
            RenderFragmentKind.ContributeValues when inputBounds.Count == 0
                => reference.RecordedBounds,
            RenderFragmentKind.ContributeValues
                or RenderFragmentKind.Opacity
                or RenderFragmentKind.Blend
                => inputBounds[0],
            RenderFragmentKind.OpacityMask => inputBounds[0],
            RenderFragmentKind.Shader
                => ((ShaderRenderFragmentPayload)reference.Payload!).Description.Bounds
                    .TransformBounds(inputBounds[0]),
            RenderFragmentKind.Geometry
                => ((GeometryRenderFragmentPayload)reference.Payload!).Description.Bounds
                    .TransformBounds(inputBounds[0]),
            RenderFragmentKind.OpaqueSource
                or RenderFragmentKind.OpaqueMap
                or RenderFragmentKind.OpaqueCombine
                or RenderFragmentKind.OpaqueExpand
                => ((OpaqueRenderFragmentPayload)reference.Payload!).Description.Bounds
                    .TransformBounds(inputBounds),
            RenderFragmentKind.LegacyFilterEffect
                => ResolveLegacyFilterBounds(reference, inputBounds),
            RenderFragmentKind.Layer
                => ResolveLayerBounds(
                    reference,
                    ((LayerRenderFragmentPayload)reference.Payload!).Domain
                    ?? throw new InvalidOperationException(
                        "An owning-domain Layer must resolve before finite bounds mapping.")),
            RenderFragmentKind.TargetLayerScope => UnionInputBounds(reference),
            RenderFragmentKind.TargetScope
                => ((TargetScopeRenderFragmentPayload)reference.Payload!).Description.Bounds
                    .TransformBounds(inputBounds[0]),
            RenderFragmentKind.RawTargetScope
                => ((RawTargetScopeRenderFragmentPayload)reference.Payload!).Description.Bounds
                    .TransformBounds(inputBounds[0]),
            _ => reference.RecordedBounds,
        };
    }

    private static Rect ResolveLegacyFilterBounds(
        RenderFragmentReference reference,
        IReadOnlyList<Rect> inputBounds)
    {
        var payload = (LegacyFilterEffectRenderFragmentPayload)reference.Payload!;
        if (payload.BoundsItems.IsDefaultOrEmpty)
            return reference.RecordedBounds;

        Rect bounds = default;
        foreach (Rect inputBoundsItem in inputBounds)
            bounds = bounds.Union(inputBoundsItem);
        foreach (IFEItem item in payload.BoundsItems)
            bounds = item.TransformBounds(bounds);
        return bounds;
    }

    private static EffectiveScale ResolveForwardScale(
        RenderFragmentReference reference,
        Rect resolvedBounds,
        RenderRequestOptions options)
    {
        EffectiveScale[] inputScales = reference.Inputs
            .Select(static input => input.EffectiveScale)
            .ToArray();
        switch (reference.Kind)
        {
            case RenderFragmentKind.ContributeValues:
            case RenderFragmentKind.Opacity:
            case RenderFragmentKind.Blend:
            case RenderFragmentKind.OpacityMask:
                return inputScales[0];
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
                        return inputScales[0];

                    return ResolveMaterializedScale(inputScales, resolvedBounds, options);
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
                        : ResolveMaterializedScale(inputScales, resolvedBounds, options);
                }
            case RenderFragmentKind.LegacyFilterEffect:
                {
                    var payload = (LegacyFilterEffectRenderFragmentPayload)reference.Payload!;
                    Rect[] inputBounds = reference.Inputs
                        .Select(static input => input.Bounds)
                        .ToArray();
                    Rect[] bufferBounds = FilterEffectWorkingScalePolicy.CalculateLegacyBufferBounds(
                        inputBounds,
                        payload.BoundsItems,
                        resolvedBounds);
                    return payload.WorkingScalePolicy is { } policy
                        ? policy.Resolve(
                            inputScales,
                            inputBounds,
                            bufferBounds,
                            options.OutputScale,
                            options.MaxWorkingScale)
                        : FilterEffectWorkingScalePolicy.ResolveMaterialized(
                            inputScales,
                            bufferBounds,
                            options.OutputScale,
                            options.MaxWorkingScale);
                }
            case RenderFragmentKind.OpaqueSource:
            case RenderFragmentKind.OpaqueMap:
            case RenderFragmentKind.OpaqueCombine:
            case RenderFragmentKind.OpaqueExpand:
                return ((OpaqueRenderFragmentPayload)reference.Payload!).Description.Scale.Resolve(
                    inputScales,
                    resolvedBounds,
                    options.OutputScale,
                    options.MaxWorkingScale);
            case RenderFragmentKind.TargetCapture:
                return ((TargetCaptureRenderFragmentPayload)reference.Payload!).Description.Scale.Resolve(
                    [],
                    resolvedBounds,
                    options.OutputScale,
                    options.MaxWorkingScale);
            case RenderFragmentKind.TargetScope:
                return ((TargetScopeRenderFragmentPayload)reference.Payload!).Description.Scale.Resolve(
                    inputScales,
                    resolvedBounds,
                    options.OutputScale,
                    options.MaxWorkingScale);
            case RenderFragmentKind.RawTargetScope:
                return ((RawTargetScopeRenderFragmentPayload)reference.Payload!).Description.Scale.Resolve(
                    inputScales,
                    resolvedBounds,
                    options.OutputScale,
                    options.MaxWorkingScale);
            default:
                return reference.RecordedEffectiveScale;
        }
    }

    private static Func<Point, bool> ResolveForwardHitTest(
        RenderFragmentReference reference,
        Rect resolvedBounds)
    {
        return reference.Kind switch
        {
            RenderFragmentKind.ContributeValues
                or RenderFragmentKind.Opacity
                or RenderFragmentKind.Blend
                or RenderFragmentKind.OpacityMask
                or RenderFragmentKind.Shader
                => reference.Inputs[0].HitTest,
            RenderFragmentKind.Geometry
                => CreateResolvedHitTest(
                    ((GeometryRenderFragmentPayload)reference.Payload!).Description.HitTest,
                    resolvedBounds,
                    reference.Inputs),
            RenderFragmentKind.OpaqueSource
                or RenderFragmentKind.OpaqueMap
                or RenderFragmentKind.OpaqueCombine
                or RenderFragmentKind.OpaqueExpand
                => CreateResolvedHitTest(
                    ((OpaqueRenderFragmentPayload)reference.Payload!).Description.HitTest,
                    resolvedBounds,
                    reference.Inputs),
            RenderFragmentKind.LegacyFilterEffect => resolvedBounds.Contains,
            RenderFragmentKind.MaterializedInput
                => CreateResolvedHitTest(
                    ((MaterializedInputRenderFragmentPayload)reference.Payload!).Description.HitTest,
                    resolvedBounds,
                    reference.Inputs),
            RenderFragmentKind.TargetCapture
                => CreateResolvedHitTest(
                    ((TargetCaptureRenderFragmentPayload)reference.Payload!).Description.HitTest,
                    resolvedBounds,
                    reference.Inputs),
            RenderFragmentKind.BuiltInBackdropCapture
                => CreateResolvedHitTest(
                    ((BuiltInBackdropCaptureRenderFragmentPayload)reference.Payload!).Description.HitTest,
                    resolvedBounds,
                    reference.Inputs),
            RenderFragmentKind.Layer or RenderFragmentKind.TargetLayerScope
                => point => reference.Inputs.Any(input => input.HitTest(point)),
            RenderFragmentKind.TargetScope
                => CreateResolvedHitTest(
                    ((TargetScopeRenderFragmentPayload)reference.Payload!).Description.HitTest,
                    resolvedBounds,
                    reference.Inputs),
            RenderFragmentKind.RawTargetScope
                => CreateResolvedHitTest(
                    ((RawTargetScopeRenderFragmentPayload)reference.Payload!).Description.HitTest,
                    resolvedBounds,
                    reference.Inputs),
            RenderFragmentKind.RawTargetCommand
                => CreateResolvedHitTest(
                    ((RawTargetCommandRenderFragmentPayload)reference.Payload!).Description.HitTest,
                    resolvedBounds,
                    reference.Inputs),
            RenderFragmentKind.TargetCommand
                => CreateResolvedHitTest(
                    ((TargetCommandRenderFragmentPayload)reference.Payload!).Description.HitTest,
                    resolvedBounds,
                    reference.Inputs),
            _ => throw new InvalidOperationException(
                $"Fragment kind '{reference.Kind}' has no symbolic hit-test lowering rule."),
        };
    }

    private static Func<Point, bool> CreateResolvedHitTest(
        RenderHitTestContract contract,
        Rect outputBounds,
        IReadOnlyList<RenderFragmentReference> inputs)
    {
        RenderHitTestInput[] views = inputs
            .Select(static input => new RenderHitTestInput(input.Bounds, input.HitTest))
            .ToArray();
        return point => contract.Evaluate(outputBounds, views, point);
    }

    private static EffectiveScale ResolveMaterializedScale(
        EffectiveScale[] inputScales,
        Rect resolvedBounds,
        RenderRequestOptions options)
    {
        float workingScale = RenderScaleUtilities.ResolveWorkingScale(
            inputScales,
            options.OutputScale,
            options.MaxWorkingScale);
        workingScale = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(
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

    private static RenderNodeMeasurement Measure(
        RenderRequestOptions options,
        IReadOnlyList<RenderFragmentReference> roots)
    {
        Rect outputBounds = default;
        Rect queryBounds = default;
        int minimum = 0;
        int? maximum = 0;
        float densestSupply = 0;
        bool hasContributingValues = false;
        bool hasTargetEffects = false;

        foreach (RenderFragmentReference root in roots)
        {
            minimum = checked(minimum + root.ValueCardinality.Minimum);
            maximum = maximum is null || root.ValueCardinality.Maximum is null
                ? null
                : checked(maximum.Value + root.ValueCardinality.Maximum.Value);
            hasContributingValues |= root.ContributesValuesToTarget;
            hasTargetEffects |= root.HasTargetEffects;

            if (root.ContributesValuesToTarget)
                outputBounds = outputBounds.Union(root.Bounds);
            if (TargetWriteMetadataResolver.Resolve(root, options.TargetDomain) is { } affected)
                outputBounds = outputBounds.Union(affected);
            queryBounds = queryBounds.Union(ResolveQueryBounds(root));

            if (!root.EffectiveScale.IsUnbounded)
                densestSupply = MathF.Max(densestSupply, root.EffectiveScale.Value);
        }

        if (options.TargetDomain is { } targetDomain)
            outputBounds = outputBounds.Intersect(targetDomain);

        EffectiveScale effectiveScale = densestSupply > 0
            ? EffectiveScale.At(densestSupply)
            : EffectiveScale.Unbounded;
        return new RenderNodeMeasurement(
            outputBounds,
            queryBounds,
            effectiveScale,
            RenderValueCardinality.Range(minimum, maximum),
            roots.Count > 0,
            hasContributingValues,
            hasTargetEffects);
    }

    private static Rect ResolveQueryBounds(RenderFragmentReference reference)
    {
        if (reference.Kind is RenderFragmentKind.TargetCapture
            or RenderFragmentKind.BuiltInBackdropCapture)
        {
            return reference.ContributesValuesToTarget ? reference.Bounds : Rect.Empty;
        }

        if (reference.Payload is TargetCommandRenderFragmentPayload command)
            return command.Description.QueryBounds;
        if (reference.Payload is RawTargetCommandRenderFragmentPayload rawCommand)
            return rawCommand.Description.QueryBounds;
        if (reference.Payload is LayerRenderFragmentPayload layerPayload)
        {
            Rect layerQuery = Rect.Empty;
            foreach (RenderFragmentReference input in reference.Inputs)
                layerQuery = layerQuery.Union(ResolveQueryBounds(input));
            return layerQuery.Intersect(layerPayload.Domain ?? reference.Bounds);
        }
        if (reference.ContributesValuesToTarget)
            return reference.Bounds;
        if (reference.Kind == RenderFragmentKind.OpacityMask)
        {
            return reference.Inputs.IsDefaultOrEmpty
                ? Rect.Empty
                : ResolveQueryBounds(reference.Inputs[0]);
        }

        Rect result = default;
        foreach (RenderFragmentReference input in reference.Inputs)
            result = result.Union(ResolveQueryBounds(input));

        if (result.Width == 0 || result.Height == 0)
            return Rect.Empty;

        return reference.Payload switch
        {
            TargetScopeRenderFragmentPayload scope
                => scope.Description.Bounds.TransformBounds(result),
            RawTargetScopeRenderFragmentPayload scope
                => scope.Description.Bounds.TransformBounds(result),
            TargetLayerScopeRenderFragmentPayload layer
                => layer.Region.Kind == TargetRegionKind.Region
                    ? result.Intersect(layer.Region.Value)
                    : layer.Region.Kind == TargetRegionKind.Empty ? Rect.Empty : result,
            _ => result,
        };
    }

    private static RequiredRegion GetRootRequirement(
        RenderFragmentReference root,
        Rect finalCommitBounds,
        Rect? targetDomain)
    {
        RequiredRegion result = RequiredRegion.Empty;
        if (root.ContributesValuesToTarget)
            result = result.Union(RequiredRegion.Region(finalCommitBounds.Intersect(root.Bounds)));

        if (TargetWriteMetadataResolver.Resolve(root, targetDomain) is { } affected)
        {
            result = result.Union(
                RequiredRegion.Region(finalCommitBounds.Intersect(affected)));
        }

        return result;
    }

    private static ImmutableArray<RequiredRegion> GetInputRequirements(
        RenderFragmentReference reference,
        RequiredRegion outputRequirement,
        Rect? targetDomain)
    {
        if (reference.Inputs.IsDefaultOrEmpty)
            return [];
        if (outputRequirement.IsEmpty)
            return ImmutableArray.CreateRange(
                Enumerable.Repeat(RequiredRegion.Empty, reference.Inputs.Length));

        return reference.Payload switch
        {
            ShaderRenderFragmentPayload shader
                => MapUnary(reference, outputRequirement, shader.Description.Bounds),
            GeometryRenderFragmentPayload geometry
                => MapUnary(reference, outputRequirement, geometry.Description.Bounds),
            TargetScopeRenderFragmentPayload scope
                => MapTargetScope(
                    reference,
                    outputRequirement,
                    scope.Description.Bounds,
                    targetDomain),
            RawTargetScopeRenderFragmentPayload
                => FullInputs(reference),
            OpaqueRenderFragmentPayload opaque
                => MapOpaque(reference, outputRequirement, opaque.Description.Bounds),
            TargetCommandRenderFragmentPayload or RawTargetCommandRenderFragmentPayload
                => FullInputs(reference),
            LegacyFilterEffectRenderFragmentPayload
                => FullInputs(reference),
            OpacityRenderFragmentPayload or BlendRenderFragmentPayload
                => MapScopedIdentityInputs(reference, outputRequirement, targetDomain),
            OpacityMaskRenderFragmentPayload
                => MapOpacityMask(reference, outputRequirement, targetDomain),
            LayerRenderFragmentPayload layer
                => MapScopeInputs(reference, outputRequirement, layer.Domain ?? reference.Bounds),
            TargetLayerScopeRenderFragmentPayload layer
                => MapScopeInputs(
                    reference,
                    outputRequirement,
                    ResolveTargetRegion(layer.Region, targetDomain)),
            _ => MapIdentityInputs(reference, outputRequirement),
        };
    }

    private static ImmutableArray<RequiredRegion> MapUnary(
        RenderFragmentReference reference,
        RequiredRegion outputRequirement,
        RenderBoundsContract bounds)
    {
        if (reference.Inputs.Length != 1)
            throw new InvalidOperationException("A unary bounds contract requires exactly one input fragment.");
        if (bounds.RequiresFullInput)
            return [RequiredRegion.Full];
        if (outputRequirement.IsFull
            && bounds.StructuralIdentity is RenderBoundsStructuralIdentity
            {
                Kind: RenderBoundsContractKind.Identity,
            })
        {
            return [RequiredRegion.Full];
        }

        Rect requested = outputRequirement.Resolve(reference.Bounds);
        Rect required = bounds.GetRequiredInputBounds(requested);
        return [RequiredRegion.Region(required.Intersect(reference.Inputs[0].Bounds))];
    }

    private static ImmutableArray<RequiredRegion> MapTargetScope(
        RenderFragmentReference reference,
        RequiredRegion outputRequirement,
        RenderBoundsContract bounds,
        Rect? targetDomain)
    {
        if (reference.Inputs.Length != 1)
            throw new InvalidOperationException("A target scope requires exactly one input fragment.");

        RequiredRegion required;
        if (bounds.RequiresFullInput)
        {
            required = RequiredRegion.Full;
        }
        else if (outputRequirement.IsFull
                 && bounds.StructuralIdentity is RenderBoundsStructuralIdentity
                 {
                     Kind: RenderBoundsContractKind.Identity,
                 })
        {
            required = RequiredRegion.Full;
        }
        else
        {
            Rect requested = outputRequirement.Resolve(ResolveSemanticBounds(reference, targetDomain));
            required = RequiredRegion.Region(bounds.GetRequiredInputBounds(requested));
        }

        Rect? inputTargetDomain = targetDomain is { } domain
            ? bounds.GetRequiredInputBounds(domain)
            : null;
        return [RestrictToSemanticCoverage(reference.Inputs[0], required, inputTargetDomain)];
    }

    private static ImmutableArray<RequiredRegion> MapOpaque(
        RenderFragmentReference reference,
        RequiredRegion outputRequirement,
        RenderOperationBoundsContract bounds)
    {
        if (RequiresFullInputs(bounds))
            return FullInputs(reference);
        if (outputRequirement.IsFull && IsIdentityMap(bounds))
            return FullInputs(reference);

        Rect requested = outputRequirement.Resolve(reference.Bounds);
        Rect[] inputBounds = reference.Inputs.Select(static input => input.Bounds).ToArray();
        IReadOnlyList<Rect> required = bounds.GetRequiredInputBounds(requested, inputBounds);
        var result = ImmutableArray.CreateBuilder<RequiredRegion>(required.Count);
        for (int index = 0; index < required.Count; index++)
        {
            result.Add(RequiredRegion.Region(required[index].Intersect(inputBounds[index])));
        }

        return result.MoveToImmutable();
    }

    private static bool RequiresFullInputs(RenderOperationBoundsContract bounds)
    {
        if (bounds.Kind == RenderOperationBoundsKind.FullInputs)
            return true;

        return bounds.StructuralIdentity is RenderOperationBoundsStructuralIdentity
        {
            Kind: RenderOperationBoundsKind.Map,
            ForwardIdentity: RenderBoundsStructuralIdentity
            {
                Kind: RenderBoundsContractKind.FullInput or RenderBoundsContractKind.CustomFullInput,
            },
        };
    }

    private static bool IsIdentityMap(RenderOperationBoundsContract bounds)
    {
        return bounds.StructuralIdentity is RenderOperationBoundsStructuralIdentity
        {
            Kind: RenderOperationBoundsKind.Map,
            ForwardIdentity: RenderBoundsStructuralIdentity
            {
                Kind: RenderBoundsContractKind.Identity,
            },
        };
    }

    private static ImmutableArray<RequiredRegion> MapScopedIdentityInputs(
        RenderFragmentReference reference,
        RequiredRegion outputRequirement,
        Rect? targetDomain)
    {
        var result = ImmutableArray.CreateBuilder<RequiredRegion>(reference.Inputs.Length);
        foreach (RenderFragmentReference input in reference.Inputs)
            result.Add(RestrictToSemanticCoverage(input, outputRequirement, targetDomain));
        return result.MoveToImmutable();
    }

    private static ImmutableArray<RequiredRegion> MapOpacityMask(
        RenderFragmentReference reference,
        RequiredRegion outputRequirement,
        Rect? targetDomain)
    {
        var result = ImmutableArray.CreateBuilder<RequiredRegion>(reference.Inputs.Length);
        result.Add(RestrictToSemanticCoverage(reference.Inputs[0], outputRequirement, targetDomain));
        for (int index = 1; index < reference.Inputs.Length; index++)
            result.Add(RequiredRegion.Full);
        return result.MoveToImmutable();
    }

    private static RequiredRegion RestrictToSemanticCoverage(
        RenderFragmentReference input,
        RequiredRegion requirement,
        Rect? targetDomain)
    {
        RequiredRegion result = requirement.Intersect(input.Bounds);
        if (TargetWriteMetadataResolver.Resolve(input, targetDomain) is { } affected)
            result = result.Union(requirement.Intersect(affected));
        return result;
    }

    private static Rect ResolveSemanticBounds(
        RenderFragmentReference reference,
        Rect? targetDomain)
    {
        Rect result = reference.Bounds;
        if (TargetWriteMetadataResolver.Resolve(reference, targetDomain) is { } affected)
            result = result.Union(affected);
        return result;
    }

    private static ImmutableArray<RequiredRegion> MapScopeInputs(
        RenderFragmentReference reference,
        RequiredRegion outputRequirement,
        Rect domain)
    {
        if (domain.Width == 0 || domain.Height == 0)
        {
            return ImmutableArray.CreateRange(
                Enumerable.Repeat(RequiredRegion.Empty, reference.Inputs.Length));
        }

        var result = ImmutableArray.CreateBuilder<RequiredRegion>(reference.Inputs.Length);
        foreach (RenderFragmentReference input in reference.Inputs)
        {
            RequiredRegion inputRequirement = RequiredRegion.Empty;
            if (input.ContributesValuesToTarget)
            {
                inputRequirement = inputRequirement.Union(
                    outputRequirement.Intersect(input.Bounds.Intersect(domain)));
            }

            if (TargetWriteMetadataResolver.Resolve(input, domain) is { } affected)
            {
                inputRequirement = inputRequirement.Union(
                    outputRequirement.Intersect(affected.Intersect(domain)));
            }

            result.Add(inputRequirement);
        }

        return result.MoveToImmutable();
    }

    private static ImmutableArray<RequiredRegion> MapIdentityInputs(
        RenderFragmentReference reference,
        RequiredRegion outputRequirement)
    {
        var result = ImmutableArray.CreateBuilder<RequiredRegion>(reference.Inputs.Length);
        foreach (RenderFragmentReference input in reference.Inputs)
        {
            result.Add(outputRequirement.IsFull
                ? RequiredRegion.Full
                : outputRequirement.Intersect(input.Bounds));
        }
        return result.MoveToImmutable();
    }

    private static ImmutableArray<RequiredRegion> FullInputs(RenderFragmentReference reference)
        => ImmutableArray.CreateRange(
            Enumerable.Repeat(RequiredRegion.Full, reference.Inputs.Length));

    private static RequiredRegion? GetTargetAccessRequirement(
        RenderFragmentReference reference,
        RequiredRegion outputRequirement,
        Rect? targetDomain)
    {
        return reference.Payload switch
        {
            TargetCaptureRenderFragmentPayload capture
                => MapTargetAccess(outputRequirement, capture.Description.SourceRegion, targetDomain),
            BuiltInBackdropCaptureRenderFragmentPayload capture
                => MapTargetAccess(outputRequirement, capture.Description.SourceRegion, targetDomain),
            TargetCommandRenderFragmentPayload command
                when command.Description.Access == TargetAccess.Readback
                => MapTargetAccess(RequiredRegion.Full, command.Description.AffectedRegion, targetDomain),
            TargetCommandRenderFragmentPayload command
                => MapTargetAccess(outputRequirement, command.Description.AffectedRegion, targetDomain),
            RawTargetCommandRenderFragmentPayload
                => outputRequirement.IsEmpty ? RequiredRegion.Empty : RequiredRegion.Full,
            RawTargetScopeRenderFragmentPayload
                => outputRequirement.IsEmpty ? RequiredRegion.Empty : RequiredRegion.Full,
            TargetLayerScopeRenderFragmentPayload layer
                => MapTargetAccess(outputRequirement, layer.Region, targetDomain),
            LayerRenderFragmentPayload layer
                => outputRequirement.Intersect(layer.Domain ?? reference.Bounds),
            _ => null,
        };
    }

    private static RequiredRegion MapTargetAccess(
        RequiredRegion requirement,
        TargetRegion access,
        Rect? targetDomain)
    {
        if (requirement.IsEmpty || access.Kind == TargetRegionKind.Empty)
            return RequiredRegion.Empty;
        if (access.Kind == TargetRegionKind.Full && requirement.IsFull)
            return RequiredRegion.Full;

        Rect domain = ResolveTargetRegion(access, targetDomain);
        return requirement.IsFull
            ? RequiredRegion.Region(domain)
            : requirement.Intersect(domain);
    }

    private static Rect ResolveTargetRegion(TargetRegion region, Rect? targetDomain)
    {
        return region.Kind switch
        {
            TargetRegionKind.Empty => Rect.Empty,
            TargetRegionKind.Region => region.Value,
            TargetRegionKind.Full when targetDomain is { } domain => domain,
            TargetRegionKind.Full => throw new InvalidOperationException(
                "A target-less request with Full target access requires a finite TargetDomain."),
            _ => throw new InvalidOperationException("The target region is uninitialized."),
        };
    }

    private static bool UnionRequirement(
        Dictionary<RenderFragmentReference, RequiredRegion> requirements,
        RenderFragmentReference reference,
        RequiredRegion requirement)
    {
        RequiredRegion previous = GetRequirement(requirements, reference);
        RequiredRegion combined = previous.Union(requirement);
        requirements[reference] = combined;
        return combined != previous;
    }

    private static RequiredRegion GetRequirement(
        Dictionary<RenderFragmentReference, RequiredRegion> requirements,
        RenderFragmentReference reference)
        => requirements.TryGetValue(reference, out RequiredRegion requirement)
            ? requirement
            : RequiredRegion.Empty;

    private static RenderFragmentId GetId(RenderFragmentReference reference)
        => reference.Id
           ?? throw new InvalidOperationException(
               "Region analysis requires every fragment to be committed to the request graph.");
}

internal sealed class RegionAnalysis
{
    public RegionAnalysis(
        RenderNodeMeasurement measurement,
        Rect? targetDomain,
        Rect? requestedRegion,
        Rect finalCommitBounds,
        RequiredRegion finalCommitRegion,
        ImmutableDictionary<RenderFragmentId, RequiredRegion> fragmentRequirements,
        ImmutableDictionary<RenderValueId, RequiredRegion> valueRequirements,
        ImmutableDictionary<RenderFragmentId, RequiredRegion> targetAccessRequirements,
        ImmutableDictionary<RenderFragmentId, ResolvedFragmentMetadata> metadata)
    {
        Measurement = measurement;
        TargetDomain = targetDomain;
        RequestedRegion = requestedRegion;
        FinalCommitBounds = finalCommitBounds;
        FinalCommitRegion = finalCommitRegion;
        FragmentRequirements = fragmentRequirements;
        ValueRequirements = valueRequirements;
        TargetAccessRequirements = targetAccessRequirements;
        Metadata = metadata;
    }

    public RenderNodeMeasurement Measurement { get; }

    public Rect RootOutputExtent => Measurement.OutputBounds;

    public Rect QueryBounds => Measurement.QueryBounds;

    public Rect? TargetDomain { get; }

    public Rect? RequestedRegion { get; }

    public Rect FinalCommitBounds { get; }

    public RequiredRegion FinalCommitRegion { get; }

    public ImmutableDictionary<RenderFragmentId, RequiredRegion> FragmentRequirements { get; }

    public ImmutableDictionary<RenderValueId, RequiredRegion> ValueRequirements { get; }

    public ImmutableDictionary<RenderFragmentId, RequiredRegion> TargetAccessRequirements { get; }

    public ImmutableDictionary<RenderFragmentId, ResolvedFragmentMetadata> Metadata { get; }

    public RequiredRegion GetFragmentRequirement(RenderFragmentReference reference)
        => FragmentRequirements[GetId(reference)];

    public RequiredRegion GetValueRequirement(RenderValueId valueId)
        => ValueRequirements[valueId];

    public RequiredRegion GetTargetAccessRequirement(RenderFragmentReference reference)
        => TargetAccessRequirements.TryGetValue(GetId(reference), out RequiredRegion requirement)
            ? requirement
            : RequiredRegion.Empty;

    public ResolvedFragmentMetadata GetMetadata(RenderFragmentReference reference)
        => Metadata[GetId(reference)];

    private static RenderFragmentId GetId(RenderFragmentReference reference)
        => reference.Id
           ?? throw new InvalidOperationException("The fragment was not committed to the request graph.");
}

internal readonly record struct ResolvedFragmentMetadata(
    Rect Bounds,
    Rect QueryBounds,
    EffectiveScale EffectiveScale);

internal readonly record struct RequiredRegion
{
    private readonly RequiredRegionKind _kind;
    private readonly Rect _value;

    private RequiredRegion(RequiredRegionKind kind, Rect value = default)
    {
        _kind = kind;
        _value = value;
    }

    public static RequiredRegion Empty { get; } = new(RequiredRegionKind.Empty);

    public static RequiredRegion Full { get; } = new(RequiredRegionKind.Full);

    public static RequiredRegion Region(Rect value)
    {
        RenderRectValidation.ThrowIfInvalidResult(
            value,
            "A required region must be finite and have non-negative dimensions.");
        return value.Width == 0 || value.Height == 0
            ? Empty
            : new RequiredRegion(RequiredRegionKind.Region, value);
    }

    public bool IsEmpty => _kind == RequiredRegionKind.Empty;

    public bool IsFull => _kind == RequiredRegionKind.Full;

    public Rect Value
        => _kind == RequiredRegionKind.Region
            ? _value
            : throw new InvalidOperationException("Only a finite required region has a Rect value.");

    public RequiredRegion Union(RequiredRegion other)
    {
        ThrowIfUninitialized();
        other.ThrowIfUninitialized();
        if (IsFull || other.IsFull)
            return Full;
        if (IsEmpty)
            return other;
        if (other.IsEmpty)
            return this;
        return Region(_value.Union(other._value));
    }

    public RequiredRegion Intersect(Rect bounds)
    {
        ThrowIfUninitialized();
        RenderRectValidation.ThrowIfInvalidInput(bounds, nameof(bounds));
        if (IsEmpty)
            return Empty;
        if (IsFull)
            return bounds.Width == 0 || bounds.Height == 0 ? Empty : Region(bounds);
        return Region(_value.Intersect(bounds));
    }

    public Rect Resolve(Rect fullBounds)
    {
        ThrowIfUninitialized();
        RenderRectValidation.ThrowIfInvalidInput(fullBounds, nameof(fullBounds));
        return _kind switch
        {
            RequiredRegionKind.Empty => Rect.Empty,
            RequiredRegionKind.Full => fullBounds,
            RequiredRegionKind.Region => _value,
            _ => throw new InvalidOperationException("The required region is uninitialized."),
        };
    }

    private void ThrowIfUninitialized()
    {
        if (_kind == RequiredRegionKind.Uninitialized)
        {
            throw new InvalidOperationException(
                "default(RequiredRegion) is uninitialized; use Empty, Full, or Region.");
        }
    }
}

internal enum RequiredRegionKind : byte
{
    Uninitialized,
    Empty,
    Full,
    Region,
}
