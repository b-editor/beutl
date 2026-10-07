using System.Collections.Immutable;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RegionAnalyzer
{
    /// <summary>
    /// Resolves forward metadata and measures the request, without deriving any region requirement.
    /// </summary>
    /// <remarks>
    /// Resolving forward metadata mutates the symbolic fragment bounds that target-scope lowering reads, so a
    /// caller that goes on to compile must re-lower and call <see cref="Analyze"/>. Requirements derived here
    /// would come from the pre-mutation dependency plan and be discarded, so this entry point does not spend
    /// the propagation that produces them.
    /// </remarks>
    public RenderNodeMeasurement ResolveMeasurement(
        RenderRequestOptions options,
        IReadOnlyList<RenderFragmentReference> roots,
        TargetDependencyPlan? targetDependencies = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(roots);

        return ResolveMetadataPass(options, roots, targetDependencies).Measurement;
    }

    public RegionAnalysis Analyze(
        RenderRequestOptions options,
        IReadOnlyList<RenderFragmentReference> roots,
        TargetDependencyPlan? targetDependencies = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(roots);

        MetadataPass pass = ResolveMetadataPass(options, roots, targetDependencies);
        targetDependencies = pass.TargetDependencies;
        ImmutableArray<RenderFragmentReference> topologicalOrder = pass.TopologicalOrder;
        ImmutableHashSet<RenderFragmentId> backingTargetBackdropCaptures = pass.BackingTargetBackdropCaptures;
        IReadOnlyDictionary<RenderFragmentReference, Rect?> targetDomains = pass.TargetDomains;
        ImmutableDictionary<RenderFragmentId, ResolvedFragmentMetadata> metadata = pass.Metadata;
        RenderNodeMeasurement measurement = pass.Measurement;
        Rect finalCommitBounds = options.RequestedRegion switch
        {
            { Width: 0 } empty => empty,
            { Height: 0 } empty => empty,
            { } requested => requested.Intersect(measurement.OutputBounds),
            null => measurement.OutputBounds,
        };
        _ = RequiredRegion.Region(finalCommitBounds);

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

        // Both maps are derived from the immutable dependency plan, so they are invariant across the
        // propagation passes below.
        IReadOnlyDictionary<TargetTokenId, TargetDependencyStep> tokenProducers = targetDependencies.Steps
            .ToDictionary(static step => step.OutputToken);
        IReadOnlyDictionary<TargetScopeId, TargetScopePlan> targetScopes = targetDependencies.Scopes
            .ToDictionary(static scope => scope.Id);
        int remainingPasses = checked(topologicalOrder.Length + targetDependencies.Steps.Length + 1);
        bool changed;
        do
        {
            changed = PropagateFragmentRequirements(
                topologicalOrder,
                targetDomains,
                fragmentRequirements,
                targetRequirements);
            changed |= PropagateTargetTokenRequirements(
                targetDependencies,
                tokenProducers,
                targetScopes,
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
        var targetAccessRegions = ImmutableDictionary.CreateBuilder<RenderFragmentId, RequiredRegion>();
        foreach (RenderFragmentReference reference in topologicalOrder)
        {
            RenderFragmentId fragmentId = GetId(reference);
            RequiredRegion requirement = GetRequirement(fragmentRequirements, reference);
            fragmentRegions.Add(fragmentId, requirement);

            if (targetRequirements.TryGetValue(reference, out RequiredRegion targetRequirement))
                targetAccessRegions.Add(fragmentId, targetRequirement);
        }

        return new RegionAnalysis(
            measurement,
            finalCommitBounds,
            fragmentRegions.ToImmutable(),
            targetAccessRegions.ToImmutable(),
            metadata,
            backingTargetBackdropCaptures);
    }

    private static MetadataPass ResolveMetadataPass(
        RenderRequestOptions options,
        IReadOnlyList<RenderFragmentReference> roots,
        TargetDependencyPlan? targetDependencies)
    {
        targetDependencies ??= TargetDependencyLowerer.Lower(
            roots.ToImmutableArray(),
            options.TargetDomain);

        ImmutableArray<RenderFragmentReference> topologicalOrder = GetTopologicalOrder(roots);
        ImmutableHashSet<RenderFragmentId> backingTargetBackdropCaptures =
            FindBackingTargetBackdropCaptures(topologicalOrder, targetDependencies);
        IReadOnlyDictionary<RenderFragmentReference, Rect?> targetDomains =
            ResolveTargetDomains(
                roots,
                options.TargetDomain,
                targetDependencies,
                backingTargetBackdropCaptures);
        ImmutableDictionary<RenderFragmentId, ResolvedFragmentMetadata> metadata =
            ResolveForwardMetadata(topologicalOrder, targetDomains, options);
        return new MetadataPass(
            targetDependencies,
            topologicalOrder,
            backingTargetBackdropCaptures,
            targetDomains,
            metadata,
            Measure(options, roots));
    }

    private readonly record struct MetadataPass(
        TargetDependencyPlan TargetDependencies,
        ImmutableArray<RenderFragmentReference> TopologicalOrder,
        ImmutableHashSet<RenderFragmentId> BackingTargetBackdropCaptures,
        IReadOnlyDictionary<RenderFragmentReference, Rect?> TargetDomains,
        ImmutableDictionary<RenderFragmentId, ResolvedFragmentMetadata> Metadata,
        RenderNodeMeasurement Measurement);

    private static bool PropagateFragmentRequirements(
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
        IReadOnlyDictionary<TargetTokenId, TargetDependencyStep> producers,
        IReadOnlyDictionary<TargetScopeId, TargetScopePlan> scopes,
        IReadOnlyDictionary<RenderFragmentId, RenderFragmentReference> referencesById,
        IReadOnlyDictionary<RenderFragmentReference, RequiredRegion> targetRequirements,
        Dictionary<RenderFragmentReference, RequiredRegion> fragmentRequirements)
    {
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
        Rect mapped = requirement.IsFull
            ? requirement.Resolve(
                sourceScope.ResolvedDomain
                ?? throw new InvalidOperationException(
                    "A Full target-token requirement cannot cross an unresolved target scope."))
            : requirement.Value;

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
                      && referencesById[ownerId].TryGetScopeBoundsContract(out RenderBoundsContract bounds)
            ? bounds.TransformBounds(requirement)
            : requirement;
        return mapped;
    }

    private static Rect MapParentToChild(
        Rect requirement,
        TargetScopePlan child,
        IReadOnlyDictionary<RenderFragmentId, RenderFragmentReference> referencesById)
    {
        Rect mapped = child.OwnerFragmentId is { } ownerId
                      && referencesById[ownerId].TryGetScopeBoundsContract(out RenderBoundsContract bounds)
            ? bounds.GetRequiredInputBounds(requirement)
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

    private static RenderFragmentId GetId(RenderFragmentReference reference)
        => reference.Id
           ?? throw new InvalidOperationException(
               "Region analysis requires every fragment to be committed to the request graph.");
}
