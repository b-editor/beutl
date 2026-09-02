using System.Collections.Immutable;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

internal sealed class CompiledRenderRequest : IDisposable
{
    public CompiledRenderRequest(
        RenderRequest request,
        RecordedRenderGraph graph,
        RegionAnalysis regions,
        ImmutableArray<RenderFragmentReference> roots,
        IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> materializationDemands,
        IReadOnlySet<RenderFragmentReference> materializedFragments,
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
        MaterializedFragments = materializedFragments
            ?? throw new ArgumentNullException(nameof(materializedFragments));
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

    public IReadOnlySet<RenderFragmentReference> MaterializedFragments { get; }

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

internal sealed class ExecutionIslandPlan
{
    private readonly Dictionary<RenderFragmentId, ExecutionIslandMembership> _membershipByFragment;

    public ExecutionIslandPlan(
        ImmutableArray<ExecutionIsland> islands,
        ImmutableArray<ExecutionIslandBoundary> boundaries)
    {
        Islands = islands;
        Boundaries = boundaries;
        _membershipByFragment = [];
        for (int index = 0; index < islands.Length; index++)
        {
            ExecutionIsland island = islands[index];
            // A plan holds few islands and this constructor runs on every plan rebind, so the pairwise scan
            // is cheaper than the set it replaced - the same trade the fragment check below already makes.
            for (int earlier = 0; earlier < index; earlier++)
            {
                if (islands[earlier].Id.Equals(island.Id))
                    throw new ArgumentException("Execution-island IDs must be unique.", nameof(islands));
            }

            ValidateIsland(island, nameof(islands));
            for (int fragment = 0; fragment < island.Fragments.Length; fragment++)
            {
                RenderFragmentId fragmentId = island.Fragments[fragment];
                bool terminal = fragment == island.Fragments.Length - 1;
                if (!_membershipByFragment.TryAdd(
                        fragmentId,
                        new ExecutionIslandMembership(island, island.ShaderRun, terminal)))
                {
                    throw new ArgumentException(
                        "A fragment cannot belong to more than one execution island.",
                        nameof(islands));
                }
            }
        }
    }

    public ImmutableArray<ExecutionIsland> Islands { get; }

    public ImmutableArray<ExecutionIslandBoundary> Boundaries { get; }

    public IEnumerable<CompiledShaderRun> ShaderRuns
        => Islands
            .Where(static island => island.ShaderRun is not null)
            .Select(static island => island.ShaderRun!);

    public bool TryGetMembership(
        RenderFragmentReference fragment,
        out ExecutionIslandMembership membership)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        if (fragment.Id is not { } id)
            throw new InvalidOperationException("An execution-plan fragment is not committed.");
        return _membershipByFragment.TryGetValue(id, out membership);
    }

    public ExecutionIslandExecutionLedger CreateExecutionLedger(
        RecordedRenderGraph graph,
        ImmutableArray<RenderFragmentReference> roots,
        RenderCacheResolution cacheResolution)
        => new(this, graph, roots, cacheResolution);

    // A cached structural plan rebinds through this constructor on every hit, so both scans stay
    // allocation-free rather than moving behind a Debug gate: a plugin-authored island is validated in the
    // build a plugin author ships against.
    private static bool HasDuplicateFragment(ImmutableArray<RenderFragmentId> fragments)
    {
        const int PairwiseScanLimit = 32;
        if (fragments.Length > PairwiseScanLimit)
        {
            var seen = new HashSet<RenderFragmentId>(fragments.Length);
            foreach (RenderFragmentId fragment in fragments)
            {
                if (!seen.Add(fragment))
                    return true;
            }

            return false;
        }

        for (int index = 0; index < fragments.Length; index++)
        {
            for (int other = index + 1; other < fragments.Length; other++)
            {
                if (fragments[index] == fragments[other])
                    return true;
            }
        }

        return false;
    }

    private static bool MatchesStageOrder(
        ImmutableArray<RenderFragmentId> fragments,
        ImmutableArray<CompiledShaderStage> stages)
    {
        if (fragments.Length != stages.Length)
            return false;

        for (int index = 0; index < fragments.Length; index++)
        {
            if (fragments[index] != stages[index].FragmentId)
                return false;
        }

        return true;
    }

    private static void ValidateIsland(ExecutionIsland island, string parameterName)
    {
        if (HasDuplicateFragment(island.Fragments))
            throw new ArgumentException("An execution island cannot contain a fragment more than once.", parameterName);

        if (island.ShaderRun is not { } run)
        {
            if (island.Fragments.Length != 1)
            {
                throw new ArgumentException(
                    "A non-Shader execution island must identify exactly one semantic fragment.",
                    parameterName);
            }
            return;
        }

        if (!MatchesStageOrder(island.Fragments, run.Stages))
        {
            throw new ArgumentException(
                "A Shader-run island must contain exactly its compiled stages in execution order.",
                parameterName);
        }
        if (run.Output.Id != island.Fragments[^1])
            throw new ArgumentException("A Shader run must publish its final stage.", parameterName);

        RenderFragmentReference current = run.Output;
        for (int index = run.Stages.Length - 1; index >= 0; index--)
        {
            CompiledShaderStage stage = run.Stages[index];
            if (!ReferenceEquals(current, stage.Fragment)
                || current.Id != stage.FragmentId
                || current.Kind != stage.Kind
                || current.Inputs.Length != 1)
            {
                throw new ArgumentException(
                    "A Shader run must describe one direct single-input semantic chain.",
                    parameterName);
            }
            current = current.Inputs[0];
        }
        if (!ReferenceEquals(current, run.Input))
            throw new ArgumentException("A Shader run has a mismatched declared input.", parameterName);
    }
}

internal readonly record struct ExecutionIslandMembership(
    ExecutionIsland Island,
    CompiledShaderRun? ShaderRun,
    bool IsTerminal);

internal sealed class ExecutionIslandExecutionLedger
{
    private readonly ExecutionIslandPlan _plan;
    private readonly Dictionary<ExecutionIslandId, int> _expectedCompletionOrder;
    private readonly HashSet<ExecutionIslandId> _active = [];
    private readonly HashSet<ExecutionIslandId> _completed = [];
    private int _lastCompletedOrder = -1;

    public ExecutionIslandExecutionLedger(
        ExecutionIslandPlan plan,
        RecordedRenderGraph graph,
        ImmutableArray<RenderFragmentReference> roots,
        RenderCacheResolution cacheResolution)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(cacheResolution);
        if (roots.IsDefault)
            throw new ArgumentException("Publication roots must be initialized.", nameof(roots));

        var graphReferences = new Dictionary<RenderFragmentId, RenderFragmentReference>();
        foreach (RecordedRenderFragment recorded in graph.Fragments)
        {
            if (recorded.Payload is not RenderFragmentReference reference || reference.Id != recorded.Id)
            {
                throw new InvalidOperationException(
                    "The execution graph contains a fragment without its committed semantic reference.");
            }
            graphReferences.Add(recorded.Id, reference);
        }

        HashSet<RenderFragmentId> cacheHits = cacheResolution.CollectPrunedHitProducers();
        HashSet<RenderFragmentReference> reachable = GetReachableReferences(
            roots,
            graphReferences,
            cacheHits);
        foreach (ExecutionIsland island in plan.Islands)
        {
            foreach (RenderFragmentId fragmentId in island.Fragments)
            {
                if (!graphReferences.TryGetValue(fragmentId, out RenderFragmentReference? reference)
                    || !reachable.Contains(reference))
                {
                    throw new InvalidOperationException(
                        "An execution island contains a fragment that is not reachable from publication roots.");
                }
            }
        }

        foreach (RenderFragmentReference reference in reachable)
        {
            RenderFragmentId id = reference.Id!.Value;
            if (cacheHits.Contains(id)
                || reference.Kind is RenderFragmentKind.ContributeValues or RenderFragmentKind.MaterializedInput)
            {
                continue;
            }
            if (!plan.TryGetMembership(reference, out _))
            {
                throw new InvalidOperationException(
                    $"Executable fragment '{id.Value}' is not assigned to an execution island.");
            }
        }

        var expected = new List<ExecutionIsland>();
        var emitted = new HashSet<ExecutionIslandId>();
        var visited = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
        var visiting = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
        foreach (RenderFragmentReference root in roots)
            Visit(root, cacheHits, expected, emitted, visited, visiting);

        if (emitted.Count != plan.Islands.Length)
        {
            throw new InvalidOperationException(
                "Every planned execution island must be reachable in publication dependency order.");
        }
        _expectedCompletionOrder = expected
            .Select(static (island, index) => (island.Id, index))
            .ToDictionary(static item => item.Id, static item => item.index);
    }

    public ExecutionIsland Begin(RenderFragmentReference fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        if (!_plan.TryGetMembership(fragment, out ExecutionIslandMembership membership))
            throw new InvalidOperationException("The executable fragment is not assigned to an execution island.");
        if (membership.ShaderRun is not null && !membership.IsTerminal)
        {
            throw new InvalidOperationException(
                "A non-terminal Shader stage cannot execute independently of its compiled island.");
        }

        ExecutionIsland island = membership.Island;
        if (_completed.Contains(island.Id) || !_active.Add(island.Id))
            throw new InvalidOperationException("An execution island cannot execute more than once.");
        return island;
    }

    public void Complete(ExecutionIsland island)
    {
        ArgumentNullException.ThrowIfNull(island);
        if (!_active.Remove(island.Id))
            throw new InvalidOperationException("Only an active execution island can complete.");
        if (!_completed.Add(island.Id))
            throw new InvalidOperationException("An execution island cannot complete more than once.");
        if (!_expectedCompletionOrder.TryGetValue(island.Id, out int order))
            throw new InvalidOperationException("The completed execution island is not part of the request schedule.");
        if (order <= _lastCompletedOrder)
        {
            throw new InvalidOperationException(
                "Execution islands completed outside dependency and painter order.");
        }
        _lastCompletedOrder = order;
    }

    public void AbandonActive()
    {
        _active.Clear();
    }

    public ImmutableArray<ExecutionIslandId> CaptureActiveIslands() => [.. _active];

    /// <summary>
    /// Abandons only the islands that became active after <paramref name="captured"/> was taken, so a failed
    /// nested execution leaves its enclosing islands free to complete.
    /// </summary>
    public void AbandonIslandsSince(ImmutableArray<ExecutionIslandId> captured)
    {
        _active.IntersectWith(captured);
    }

    public void ValidateCompleted(
        bool allowSkippedIslands = false,
        IReadOnlySet<ExecutionIslandId>? regionEmptyIslands = null)
    {
        if (_active.Count != 0)
            throw new InvalidOperationException("An execution island was left active at request completion.");
        if (allowSkippedIslands)
            return;

        bool hasIncompleteIsland = false;
        foreach (ExecutionIslandId id in _expectedCompletionOrder.Keys)
        {
            if (!_completed.Contains(id)
                && (regionEmptyIslands is null || !regionEmptyIslands.Contains(id)))
            {
                hasIncompleteIsland = true;
                break;
            }
        }

        if (hasIncompleteIsland)
        {
            throw new InvalidOperationException(
                "Every scheduled execution island must complete before request publication.");
        }
    }

    private void Visit(
        RenderFragmentReference reference,
        IReadOnlySet<RenderFragmentId> cacheHits,
        ICollection<ExecutionIsland> expected,
        ISet<ExecutionIslandId> emitted,
        ISet<RenderFragmentReference> visited,
        ISet<RenderFragmentReference> visiting)
    {
        if (visiting.Contains(reference))
            throw new InvalidOperationException("The execution graph contains a dependency cycle.");
        if (!visited.Add(reference))
            return;
        visiting.Add(reference);
        try
        {
            RenderFragmentId id = reference.Id
                ?? throw new InvalidOperationException("An execution fragment is not committed.");
            if (cacheHits.Contains(id))
                return;

            if (_plan.TryGetMembership(reference, out ExecutionIslandMembership membership))
            {
                if (membership.ShaderRun is { } run)
                {
                    if (!membership.IsTerminal)
                    {
                        throw new InvalidOperationException(
                            "A non-terminal Shader stage cannot be scheduled as an independent entry point.");
                    }
                    Visit(run.Input, cacheHits, expected, emitted, visited, visiting);
                }
                else
                {
                    foreach (RenderFragmentReference input in EnumerateExecutionInputs(reference))
                        Visit(input, cacheHits, expected, emitted, visited, visiting);
                }

                if (emitted.Add(membership.Island.Id))
                    expected.Add(membership.Island);
                return;
            }

            foreach (RenderFragmentReference input in EnumerateExecutionInputs(reference))
                Visit(input, cacheHits, expected, emitted, visited, visiting);
        }
        finally
        {
            visiting.Remove(reference);
        }
    }

    private static IEnumerable<RenderFragmentReference> EnumerateExecutionInputs(
        RenderFragmentReference reference)
    {
        ImmutableArray<RenderFragmentReference> inputs = reference.ExecutionInputs;
        if (reference.Kind == RenderFragmentKind.OpacityMask && inputs.Length > 1)
        {
            for (int index = 1; index < inputs.Length; index++)
                yield return inputs[index];
            yield return inputs[0];
            yield break;
        }

        foreach (RenderFragmentReference input in inputs)
            yield return input;
    }

    private static HashSet<RenderFragmentReference> GetReachableReferences(
        ImmutableArray<RenderFragmentReference> roots,
        IReadOnlyDictionary<RenderFragmentId, RenderFragmentReference> graphReferences,
        IReadOnlySet<RenderFragmentId> cacheHits)
    {
        var result = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
        // Pushing in reverse leaves the roots on top in their declared order, which is what Reverse() was
        // for; going through LINQ boxes the ImmutableArray and buffers a copy on every frame.
        var pending = new Stack<RenderFragmentReference>(roots.Length);
        for (int index = roots.Length - 1; index >= 0; index--)
            pending.Push(roots[index]);
        while (pending.TryPop(out RenderFragmentReference? reference))
        {
            RenderFragmentId id = reference.Id
                ?? throw new InvalidOperationException("A publication root is not committed.");
            if (!graphReferences.TryGetValue(id, out RenderFragmentReference? graphReference)
                || !ReferenceEquals(reference, graphReference))
            {
                throw new ArgumentException("A publication root is not part of the recorded graph.", nameof(roots));
            }
            if (!result.Add(reference) || cacheHits.Contains(id))
                continue;
            ImmutableArray<RenderFragmentReference> inputs = reference.ExecutionInputs;
            for (int index = inputs.Length - 1; index >= 0; index--)
                pending.Push(inputs[index]);
        }
        return result;
    }
}

internal sealed class ExecutionIsland
{
    public ExecutionIsland(
        ExecutionIslandId id,
        ExecutionIslandKind kind,
        ImmutableArray<RenderFragmentId> fragments,
        bool plansGpuPass,
        CompiledShaderRun? shaderRun = null)
    {
        if (id.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(id));
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (fragments.IsDefaultOrEmpty)
            throw new ArgumentException("An execution island must contain at least one fragment.", nameof(fragments));
        if ((kind == ExecutionIslandKind.ShaderRun) != (shaderRun is not null))
        {
            throw new ArgumentException(
                "Only Shader-run islands carry a compiled Shader run.",
                nameof(shaderRun));
        }
        if (kind == ExecutionIslandKind.ShaderRun && !plansGpuPass)
            throw new ArgumentException("A Shader-run island must plan one GPU pass.", nameof(plansGpuPass));

        Id = id;
        Kind = kind;
        Fragments = fragments;
        PlansGpuPass = plansGpuPass;
        ShaderRun = shaderRun;
    }

    public ExecutionIslandId Id { get; }

    public ExecutionIslandKind Kind { get; }

    public ImmutableArray<RenderFragmentId> Fragments { get; }

    public bool PlansGpuPass { get; }

    public CompiledShaderRun? ShaderRun { get; }
}

internal sealed class CompiledShaderRun
{
    public CompiledShaderRun(
        CompiledShaderRunId id,
        RenderFragmentReference input,
        RenderFragmentReference output,
        ImmutableArray<CompiledShaderStage> stages,
        SkslMergedProgram program,
        ShaderRunCoverageSource coverageSource)
    {
        if (id.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(id));
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        if (stages.IsDefaultOrEmpty)
            throw new ArgumentException("A compiled Shader run must contain at least one stage.", nameof(stages));
        ArgumentNullException.ThrowIfNull(program);
        if (program.RequiresStandaloneExecution)
        {
            throw new ArgumentException(
                "A backend-overflowing program must remain a compatibility boundary.",
                nameof(program));
        }
        if (program.StageCount != stages.Length)
            throw new ArgumentException("The merged program and semantic stage counts must match.", nameof(program));
        if (!Enum.IsDefined(coverageSource))
            throw new ArgumentOutOfRangeException(nameof(coverageSource));

        ShaderDescription? wholeSourceHead = stages[0].Description.Kind == ShaderDescriptionKind.WholeSource
            ? stages[0].Description
            : null;
        for (int index = wholeSourceHead is null ? 0 : 1; index < stages.Length; index++)
        {
            if (stages[index].Description.Kind != ShaderDescriptionKind.WholeSource)
                continue;

            throw new ArgumentException(
                "A WholeSource shader can appear only at the head of a compiled Shader run.",
                nameof(stages));
        }
        if (wholeSourceHead is not null
            && (!output.Bounds.Equals(stages[0].Fragment.Bounds)
                || !output.EffectiveScale.Equals(stages[0].Fragment.EffectiveScale)))
        {
            throw new ArgumentException(
                "A WholeSource-headed run must preserve the head stage's output bounds and effective scale.",
                nameof(output));
        }

        Id = id;
        Input = input;
        Output = output;
        Stages = stages;
        Program = program;
        CoverageSource = coverageSource;
        WholeSourceHead = wholeSourceHead;
    }

    public CompiledShaderRunId Id { get; }

    public RenderFragmentReference Input { get; }

    public RenderFragmentReference Output { get; }

    public ImmutableArray<CompiledShaderStage> Stages { get; }

    public SkslMergedProgram Program { get; }

    /// <summary>Gets the WholeSource head whose implicit source mapping governs the run input, if present.</summary>
    public ShaderDescription? WholeSourceHead { get; }

    /// <summary>
    /// Gets the compile-time witness for the run input's coverage provenance. The executor still
    /// consumes a materialized value for every run; this witness does not authorize bypassing that
    /// runtime materialization.
    /// </summary>
    public ShaderRunCoverageSource CoverageSource { get; }

    public bool IsFused => Stages.Length > 1;
}

internal sealed record CompiledShaderStage(
    RenderFragmentId FragmentId,
    RenderFragmentReference Fragment,
    RenderFragmentKind Kind,
    ShaderDescription Description,
    SkslCoverageBehavior CoverageBehavior,
    int ProgramStageIndex);

internal readonly record struct ExecutionIslandBoundary(
    RenderFragmentId? BeforeFragmentId,
    RenderFragmentId? AfterFragmentId,
    ExecutionIslandBoundaryReason Reason,
    ImmutableArray<SkslBackendLimit> BackendLimits);

internal readonly record struct ExecutionIslandId(int Value);

internal readonly record struct CompiledShaderRunId(int Value);

internal enum ExecutionIslandKind : byte
{
    ShaderRun,
    Compatibility,
    Target,
    Readback,
}

internal enum ShaderRunCoverageSource : byte
{
    MaterializedInput,
    PriorShaderRun,
    CompatibilityMaterialization,
    EngineHomogeneousProof,
}

internal enum ExecutionIslandBoundaryReason : byte
{
    MaterializedInput,
    CoverageResolution,
    WholeSourceShader,
    Geometry,
    Opaque,
    CustomEffectItem,
    TargetCommand,
    TargetCapture,
    TargetScope,
    Layer,
    Readback,
    UnsafeComposite,
    SemanticComposite,
    RawCanvas,
    CacheInput,
    CacheCapture,
    BackendTransition,
    ThreeD,
    DynamicTopology,
    ScopeMismatch,
    ScaleTransition,
    Branching,
    FusionDisabled,
    BackendLimit,
    FilterEffectSegment,
}

internal sealed class TargetDependencyPlan
{
    public TargetDependencyPlan(
        ImmutableArray<TargetDependencyStep> steps,
        ImmutableArray<TargetScopePlan> scopes)
    {
        Steps = steps;
        Scopes = scopes;
    }

    public ImmutableArray<TargetDependencyStep> Steps { get; }

    public ImmutableArray<TargetScopePlan> Scopes { get; }
}

internal readonly record struct TargetScopeId(int Value);

internal readonly record struct TargetTokenId(int Value);

internal readonly record struct TargetDependencyStep(
    RenderFragmentId FragmentId,
    TargetScopeId ScopeId,
    TargetTokenId InputToken,
    TargetTokenId OutputToken,
    RenderValueId? TargetReadValueId,
    RenderValueId? ProducedValueId,
    TargetDependencyKind Kind);

internal readonly record struct TargetScopePlan(
    TargetScopeId Id,
    TargetScopeId? ParentId,
    RenderFragmentId? OwnerFragmentId,
    TargetTokenId InitialToken,
    Rect? ResolvedDomain,
    bool IsOrderOnly);

internal enum TargetDependencyKind : byte
{
    Composite,
    Command,
    Capture,
    ScopeComposite,
}
