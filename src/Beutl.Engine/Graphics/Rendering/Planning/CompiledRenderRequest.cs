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

        IReadOnlyDictionary<RenderFragmentId, RenderFragmentReference> references = graph.Fragments
            .ToDictionary(
                static fragment => fragment.Id,
                static fragment => (RenderFragmentReference)fragment.Payload!);
        IReadOnlyDictionary<TargetScopeId, TargetScopePlan> scopes = targetDependencies.Scopes
            .ToDictionary(static scope => scope.Id);
        var scopesByOwner = targetDependencies.Scopes
            .Where(static scope => scope.OwnerFragmentId is not null)
            .GroupBy(static scope => scope.OwnerFragmentId!.Value)
            .ToDictionary(static group => group.Key, static group => group.ToArray());
        var scopesByEffect = targetDependencies.Steps
            .GroupBy(static step => step.FragmentId)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static step => step.ScopeId).Distinct().ToArray());
        var tokens = new TargetTokenConnectivity(targetDependencies);

        foreach ((RenderFragmentId fragmentId, RequiredRegion requirement)
                 in regions.TargetAccessRequirements)
        {
            if (requirement.IsEmpty)
                continue;

            TargetScopeId[] accessScopes = scopesByOwner.TryGetValue(fragmentId, out TargetScopePlan[]? owned)
                ? owned.Select(static scope => scope.Id).ToArray()
                : scopesByEffect.TryGetValue(fragmentId, out TargetScopeId[]? effected)
                    ? effected
                    : throw new InvalidOperationException(
                        "A target-access requirement has no lowered target scope.");
            foreach (TargetScopeId accessScopeId in accessScopes)
            {
                TargetScopePlan accessScope = scopes[accessScopeId];
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
        foreach (CompiledRenderRequest nestedRequest in NestedRequests.Reverse())
            nestedRequest.Dispose();
        Request.Dispose();
    }
}

internal sealed class ExecutionIslandPlan
{
    private readonly Dictionary<RenderFragmentReference, CompiledShaderRun> _shaderRunsByOutput;
    private readonly Dictionary<RenderFragmentId, ExecutionIslandMembership> _membershipByFragment;

    public ExecutionIslandPlan(
        ImmutableArray<ExecutionIsland> islands,
        ImmutableArray<ExecutionIslandBoundary> boundaries)
    {
        Islands = islands;
        Boundaries = boundaries;
        _shaderRunsByOutput = new Dictionary<RenderFragmentReference, CompiledShaderRun>(
            ReferenceEqualityComparer.Instance);
        _membershipByFragment = [];
        var islandIds = new HashSet<ExecutionIslandId>();
        foreach (ExecutionIsland island in islands)
        {
            if (!islandIds.Add(island.Id))
                throw new ArgumentException("Execution-island IDs must be unique.", nameof(islands));

            ValidateIsland(island, nameof(islands));
            for (int index = 0; index < island.Fragments.Length; index++)
            {
                RenderFragmentId fragmentId = island.Fragments[index];
                bool terminal = index == island.Fragments.Length - 1;
                if (!_membershipByFragment.TryAdd(
                        fragmentId,
                        new ExecutionIslandMembership(island, island.ShaderRun, terminal)))
                {
                    throw new ArgumentException(
                        "A fragment cannot belong to more than one execution island.",
                        nameof(islands));
                }
            }

            if (island.ShaderRun is not { } run)
                continue;

            if (!_shaderRunsByOutput.TryAdd(run.Output, run))
            {
                throw new ArgumentException(
                    "Two compiled Shader runs cannot publish the same terminal fragment.",
                    nameof(islands));
            }
        }
    }

    public ImmutableArray<ExecutionIsland> Islands { get; }

    public ImmutableArray<ExecutionIslandBoundary> Boundaries { get; }

    public IEnumerable<CompiledShaderRun> ShaderRuns
        => Islands
            .Where(static island => island.ShaderRun is not null)
            .Select(static island => island.ShaderRun!);

    public bool TryGetShaderRun(
        RenderFragmentReference output,
        out CompiledShaderRun? run)
    {
        ArgumentNullException.ThrowIfNull(output);
        return _shaderRunsByOutput.TryGetValue(output, out run);
    }

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

    private static void ValidateIsland(ExecutionIsland island, string parameterName)
    {
        if (island.Fragments.Distinct().Count() != island.Fragments.Length)
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

        if (!island.Fragments.SequenceEqual(run.Stages.Select(static stage => stage.FragmentId)))
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

        var cacheHits = new HashSet<RenderFragmentId>(
            cacheResolution.Hits.Select(static hit => hit.OriginalProducerId));
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

    public void ValidateCompleted(bool allowSkippedIslands = false)
    {
        if (_active.Count != 0)
            throw new InvalidOperationException("An execution island was left active at request completion.");
        if (!allowSkippedIslands && _completed.Count != _expectedCompletionOrder.Count)
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
        if (reference.Kind == RenderFragmentKind.OpacityMask && reference.Inputs.Length > 1)
        {
            for (int index = 1; index < reference.Inputs.Length; index++)
                yield return reference.Inputs[index];
            yield return reference.Inputs[0];
            yield break;
        }

        foreach (RenderFragmentReference input in reference.Inputs)
            yield return input;
    }

    private static HashSet<RenderFragmentReference> GetReachableReferences(
        ImmutableArray<RenderFragmentReference> roots,
        IReadOnlyDictionary<RenderFragmentId, RenderFragmentReference> graphReferences,
        IReadOnlySet<RenderFragmentId> cacheHits)
    {
        var result = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<RenderFragmentReference>(roots.Reverse());
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
            for (int index = reference.Inputs.Length - 1; index >= 0; index--)
                pending.Push(reference.Inputs[index]);
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

        Id = id;
        Input = input;
        Output = output;
        Stages = stages;
        Program = program;
        CoverageSource = coverageSource;
    }

    public CompiledShaderRunId Id { get; }

    public RenderFragmentReference Input { get; }

    public RenderFragmentReference Output { get; }

    public ImmutableArray<CompiledShaderStage> Stages { get; }

    public SkslMergedProgram Program { get; }

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
    LegacyCustomEffect,
    TargetCommand,
    TargetCapture,
    TargetScope,
    Layer,
    Readback,
    UnsafeComposite,
    SemanticComposite,
    LegacyRawCanvas,
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
