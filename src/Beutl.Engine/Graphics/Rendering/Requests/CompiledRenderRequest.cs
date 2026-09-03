using System.Collections.Immutable;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed class CompiledRenderRequest : IDisposable
{
    public CompiledRenderRequest(
        RenderRequest request,
        RecordedRenderGraph graph,
        RegionAnalysis regions,
        ImmutableArray<RenderFragmentReference> roots,
        IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> materializationDemands,
        IReadOnlySet<RenderFragmentReference> previewDropEligibleMaterializations,
        TargetDependencyPlan targetDependencies,
        RenderCacheResolution cacheResolution,
        ExecutionIslandPlan executionPlan,
        ImmutableArray<CompiledRenderRequest> nestedRequests = default)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Graph = graph ?? throw new ArgumentNullException(nameof(graph));
        Regions = regions ?? throw new ArgumentNullException(nameof(regions));
        TargetDependencies = targetDependencies ?? throw new ArgumentNullException(nameof(targetDependencies));
        Measurement = regions.Measurement;
        SelectedOutputBounds = regions.FinalCommitBounds;
        ExecutionTargetBounds = ResolveExecutionTargetBounds(graph, regions, TargetDependencies);
        Roots = roots;
        MaterializationDemands = materializationDemands
            ?? throw new ArgumentNullException(nameof(materializationDemands));
        PreviewDropEligibleMaterializations = previewDropEligibleMaterializations
            ?? throw new ArgumentNullException(nameof(previewDropEligibleMaterializations));
        CacheResolution = cacheResolution ?? throw new ArgumentNullException(nameof(cacheResolution));
        ExecutionPlan = executionPlan ?? throw new ArgumentNullException(nameof(executionPlan));
        NestedRequests = nestedRequests.IsDefault ? [] : nestedRequests;
    }

    public RenderRequest Request { get; }

    public RecordedRenderGraph Graph { get; }

    public RenderNodeMeasurement Measurement { get; }

    public RegionAnalysis Regions { get; }

    public Rect SelectedOutputBounds { get; }

    public Rect ExecutionTargetBounds { get; }

    public ImmutableArray<RenderFragmentReference> Roots { get; }

    public IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> MaterializationDemands { get; }

    public IReadOnlySet<RenderFragmentReference> PreviewDropEligibleMaterializations { get; }

    public TargetDependencyPlan TargetDependencies { get; }

    public RenderCacheResolution CacheResolution { get; }

    public ExecutionIslandPlan ExecutionPlan { get; }

    public ImmutableArray<CompiledRenderRequest> NestedRequests { get; }

    public bool IsDisposed { get; private set; }

    private static Rect ResolveExecutionTargetBounds(
        RecordedRenderGraph graph,
        RegionAnalysis regions,
        TargetDependencyPlan targetDependencies)
    {
        Rect result = regions.FinalCommitBounds;
        if (regions.TargetAccessRequirements.Count == 0)
            return result;

        var references = new Dictionary<RenderFragmentId, RenderFragmentReference>(graph.Fragments.Length);
        foreach (RecordedRenderFragment fragment in graph.Fragments)
            references.Add(fragment.Id, (RenderFragmentReference)fragment.Payload!);

        var scopes = new Dictionary<TargetScopeId, TargetScopePlan>(targetDependencies.Scopes.Length);
        var scopesByOwner = new Dictionary<RenderFragmentId, List<TargetScopePlan>>();
        foreach (TargetScopePlan scope in targetDependencies.Scopes)
        {
            scopes.Add(scope.Id, scope);
            if (scope.OwnerFragmentId is not { } owner)
                continue;
            if (!scopesByOwner.TryGetValue(owner, out List<TargetScopePlan>? ownedScopes))
                scopesByOwner.Add(owner, ownedScopes = []);
            ownedScopes.Add(scope);
        }

        var scopesByEffect = new Dictionary<RenderFragmentId, List<TargetScopeId>>();
        foreach (TargetDependencyStep step in targetDependencies.Steps)
        {
            if (!scopesByEffect.TryGetValue(step.FragmentId, out List<TargetScopeId>? stepScopes))
                scopesByEffect.Add(step.FragmentId, stepScopes = []);
            // A step may reach the same scope more than once; the lookup wants each scope once.
            if (!stepScopes.Contains(step.ScopeId))
                stepScopes.Add(step.ScopeId);
        }

        var tokens = new TargetTokenConnectivity(targetDependencies);

        foreach ((RenderFragmentId fragmentId, RequiredRegion requirement)
                 in regions.TargetAccessRequirements)
        {
            if (requirement.IsEmpty)
                continue;

            scopesByOwner.TryGetValue(fragmentId, out List<TargetScopePlan>? owned);
            List<TargetScopeId>? effected = null;
            if (owned is null && !scopesByEffect.TryGetValue(fragmentId, out effected))
            {
                throw new InvalidOperationException(
                    "A target-access requirement has no lowered target scope.");
            }

            int accessScopeCount = owned?.Count ?? effected!.Count;
            for (int access = 0; access < accessScopeCount; access++)
            {
                TargetScopePlan accessScope = owned is not null
                    ? owned[access]
                    : scopes[effected![access]];
                Rect accessBounds = ResolveRequirement(requirement, accessScope);
                if (TryMapToRoot(
                        accessScope,
                        accessBounds,
                        scopes,
                        references,
                        tokens,
                        out Rect rootBounds))
                {
                    result = result.Union(rootBounds);
                }
            }
        }

        return result;
    }

    private static Rect ResolveRequirement(
        RequiredRegion requirement,
        TargetScopePlan scope)
    {
        if (!requirement.IsFull)
            return requirement.Value;
        if (scope.ResolvedDomain is not { } domain)
        {
            throw new InvalidOperationException(
                "A Full target-access requirement has no finite owning target domain.");
        }

        return domain;
    }

    private static bool TryMapToRoot(
        TargetScopePlan scope,
        Rect bounds,
        IReadOnlyDictionary<TargetScopeId, TargetScopePlan> scopes,
        IReadOnlyDictionary<RenderFragmentId, RenderFragmentReference> references,
        TargetTokenConnectivity tokens,
        out Rect rootBounds)
    {
        while (scope.ParentId is { } parentId)
        {
            TargetScopePlan parent = scopes[parentId];
            if (!tokens.ShareTarget(scope, parent))
            {
                rootBounds = default;
                return false;
            }

            if (scope.OwnerFragmentId is not { } ownerId
                || !references.TryGetValue(ownerId, out RenderFragmentReference? owner))
            {
                throw new InvalidOperationException(
                    "A non-root target scope has no recorded owner fragment.");
            }

            bounds = owner.Payload switch
            {
                TargetScopeRenderFragmentPayload payload
                    => payload.Description.Bounds.TransformBounds(bounds),
                RawTargetScopeRenderFragmentPayload payload
                    => payload.Description.Bounds.TransformBounds(bounds),
                _ => bounds,
            };
            if (parent.ResolvedDomain is { } parentDomain)
                bounds = bounds.Intersect(parentDomain);
            scope = parent;
        }

        rootBounds = bounds;
        return true;
    }

    private sealed class TargetTokenConnectivity
    {
        private readonly Dictionary<TargetTokenId, TargetTokenId> _parents = [];

        public TargetTokenConnectivity(TargetDependencyPlan plan)
        {
            foreach (TargetScopePlan scope in plan.Scopes)
                Add(scope.InitialToken);
            foreach (TargetDependencyStep step in plan.Steps)
            {
                Add(step.InputToken);
                Add(step.OutputToken);
                Union(step.InputToken, step.OutputToken);
            }
        }

        public bool ShareTarget(TargetScopePlan first, TargetScopePlan second)
            => Find(first.InitialToken) == Find(second.InitialToken);

        private void Add(TargetTokenId token)
            => _parents.TryAdd(token, token);

        private TargetTokenId Find(TargetTokenId token)
        {
            TargetTokenId parent = _parents[token];
            while (parent != _parents[parent])
                parent = _parents[parent];

            TargetTokenId current = token;
            while (current != parent)
            {
                TargetTokenId next = _parents[current];
                _parents[current] = parent;
                current = next;
            }

            return parent;
        }

        private void Union(TargetTokenId first, TargetTokenId second)
        {
            TargetTokenId firstRoot = Find(first);
            TargetTokenId secondRoot = Find(second);
            if (firstRoot != secondRoot)
                _parents[secondRoot] = firstRoot;
        }
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        // ImmutableArray has no instance Reverse, so the LINQ form boxes it and buffers a copy; this runs
        // on every frame's teardown.
        for (int index = NestedRequests.Length - 1; index >= 0; index--)
            NestedRequests[index].Dispose();
        Request.Dispose();
    }
}
