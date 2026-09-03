using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Rendering.Requests;

/// <summary>
/// Retains the last structural request family for a renderer. Each stable depth-first family slot keeps
/// one candidate, and the complete structural identity must compare equal before a plan is rebound to a
/// new request.
/// </summary>
internal sealed class StructuralPlanCache : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<int, Entry> _entries = [];
    private long _hits;
    private long _misses;
    private long _compilations;
    private long _replacements;
    private bool _disposed;

    public StructuralPlanCacheStatistics Statistics
    {
        get
        {
            lock (_gate)
            {
                return new StructuralPlanCacheStatistics(
                    _hits,
                    _misses,
                    _compilations,
                    _replacements,
                    _entries.Count);
            }
        }
    }

    public ExecutionIslandPlan GetOrCompile(
        StructuralPlanIdentity identity,
        RecordedRenderGraph graph,
        Func<ExecutionIslandPlan> compile,
        int familySlot = 0)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(compile);
        ArgumentOutOfRangeException.ThrowIfNegative(familySlot);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entries.TryGetValue(familySlot, out Entry? entry)
                && entry.Identity.Equals(identity))
            {
                _hits++;
                return entry.Template.Bind(graph);
            }

            _misses++;
            ExecutionIslandPlan compiled = compile();
            StructuralExecutionPlanTemplate template = StructuralExecutionPlanTemplate.Create(compiled, graph);
            if (entry is not null)
                _replacements++;
            _entries[familySlot] = new Entry(identity, template);
            _compilations++;
            return compiled;
        }
    }

    public void RetainFamilySlots(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            List<int>? staleSlots = null;
            foreach (int slot in _entries.Keys)
            {
                if (slot >= count)
                    (staleSlots ??= []).Add(slot);
            }

            if (staleSlots is null)
                return;

            foreach (int slot in staleSlots)
                _entries.Remove(slot);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _entries.Clear();
        }
    }

    private sealed record Entry(
        StructuralPlanIdentity Identity,
        StructuralExecutionPlanTemplate Template);
}

internal readonly record struct StructuralCacheBoundaryIdentity(
    int FragmentIndex,
    RenderCacheResolutionKind Kind);

internal sealed class StructuralExecutionPlanTemplate
{
    private readonly int _fragmentCount;
    private readonly IslandTemplate[] _islands;
    private readonly BoundaryTemplate[] _boundaries;

    private StructuralExecutionPlanTemplate(
        int fragmentCount,
        IslandTemplate[] islands,
        BoundaryTemplate[] boundaries)
    {
        _fragmentCount = fragmentCount;
        _islands = islands;
        _boundaries = boundaries;
    }

    public static StructuralExecutionPlanTemplate Create(
        ExecutionIslandPlan plan,
        RecordedRenderGraph graph)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(graph);
        ImmutableArray<ExecutionIsland> planIslands = plan.Islands;
        IslandTemplate[] islands = planIslands.Length == 0 ? [] : new IslandTemplate[planIslands.Length];
        for (int index = 0; index < planIslands.Length; index++)
            islands[index] = IslandTemplate.Create(planIslands[index], graph);

        ImmutableArray<ExecutionIslandBoundary> planBoundaries = plan.Boundaries;
        BoundaryTemplate[] boundaries =
            planBoundaries.Length == 0 ? [] : new BoundaryTemplate[planBoundaries.Length];
        for (int index = 0; index < planBoundaries.Length; index++)
            boundaries[index] = BoundaryTemplate.Create(planBoundaries[index], graph);

        return new StructuralExecutionPlanTemplate(graph.Fragments.Length, islands, boundaries);
    }

    public ExecutionIslandPlan Bind(RecordedRenderGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (graph.Fragments.Length != _fragmentCount)
        {
            throw new InvalidOperationException(
                "A cached structural plan cannot bind to a graph with a different fragment count.");
        }

        RenderFragmentReference[] references =
            _fragmentCount == 0 ? [] : new RenderFragmentReference[_fragmentCount];
        for (int index = 0; index < _fragmentCount; index++)
        {
            references[index] = graph.Fragments[index].Payload as RenderFragmentReference
                ?? throw new InvalidOperationException(
                    "A cached structural plan requires executable semantic fragment references.");
        }

        ExecutionIsland[] islands = _islands.Length == 0 ? [] : new ExecutionIsland[_islands.Length];
        for (int index = 0; index < _islands.Length; index++)
            islands[index] = _islands[index].Bind(graph, references);

        ExecutionIslandBoundary[] boundaries =
            _boundaries.Length == 0 ? [] : new ExecutionIslandBoundary[_boundaries.Length];
        for (int index = 0; index < _boundaries.Length; index++)
            boundaries[index] = _boundaries[index].Bind(graph);

        return new ExecutionIslandPlan(
            ImmutableCollectionsMarshal.AsImmutableArray(islands),
            ImmutableCollectionsMarshal.AsImmutableArray(boundaries));
    }

    private sealed record IslandTemplate(
        int Id,
        ExecutionIslandKind Kind,
        int[] Fragments,
        ShaderRunTemplate? ShaderRun)
    {
        public static IslandTemplate Create(
            ExecutionIsland island,
            RecordedRenderGraph graph)
        {
            ImmutableArray<RenderFragmentId> islandFragments = island.Fragments;
            int[] fragments = islandFragments.Length == 0 ? [] : new int[islandFragments.Length];
            for (int index = 0; index < islandFragments.Length; index++)
                fragments[index] = GetFragmentIndex(islandFragments[index], graph);

            return new IslandTemplate(
                island.Id.Value,
                island.Kind,
                fragments,
                island.ShaderRun is { } run ? ShaderRunTemplate.Create(run, graph) : null);
        }

        public ExecutionIsland Bind(
            RecordedRenderGraph graph,
            RenderFragmentReference[] references)
        {
            RenderFragmentId[] fragmentIds =
                Fragments.Length == 0 ? [] : new RenderFragmentId[Fragments.Length];
            for (int index = 0; index < Fragments.Length; index++)
                fragmentIds[index] = graph.Fragments[Fragments[index]].Id;

            return new ExecutionIsland(
                new ExecutionIslandId(Id),
                Kind,
                ImmutableCollectionsMarshal.AsImmutableArray(fragmentIds),
                ShaderRun?.Bind(graph, references));
        }
    }

    private sealed record ShaderRunTemplate(
        int Id,
        int Input,
        int Output,
        StageTemplate[] Stages,
        SkslMergedProgram Program)
    {
        public static ShaderRunTemplate Create(
            CompiledShaderRun run,
            RecordedRenderGraph graph)
        {
            ImmutableArray<CompiledShaderStage> runStages = run.Stages;
            StageTemplate[] stages = runStages.Length == 0 ? [] : new StageTemplate[runStages.Length];
            for (int index = 0; index < runStages.Length; index++)
                stages[index] = StageTemplate.Create(runStages[index], graph);

            return new ShaderRunTemplate(
                run.Id.Value,
                GetFragmentIndex(GetId(run.Input), graph),
                GetFragmentIndex(GetId(run.Output), graph),
                stages,
                run.Program);
        }

        public CompiledShaderRun Bind(
            RecordedRenderGraph graph,
            RenderFragmentReference[] references)
        {
            CompiledShaderStage[] stages =
                Stages.Length == 0 ? [] : new CompiledShaderStage[Stages.Length];
            for (int index = 0; index < Stages.Length; index++)
                stages[index] = Stages[index].Bind(graph, references);

            return new CompiledShaderRun(
                new CompiledShaderRunId(Id),
                references[Input],
                references[Output],
                ImmutableCollectionsMarshal.AsImmutableArray(stages),
                Program);
        }
    }

    private sealed record StageTemplate(
        int Fragment,
        RenderFragmentKind Kind,
        SkslCoverageBehavior CoverageBehavior,
        int ProgramStageIndex)
    {
        public static StageTemplate Create(
            CompiledShaderStage stage,
            RecordedRenderGraph graph)
            => new(
                GetFragmentIndex(stage.FragmentId, graph),
                stage.Kind,
                stage.CoverageBehavior,
                stage.ProgramStageIndex);

        public CompiledShaderStage Bind(
            RecordedRenderGraph graph,
            RenderFragmentReference[] references)
        {
            RenderFragmentReference reference = references[Fragment];
            if (reference.Kind != Kind)
                throw new InvalidOperationException("A cached Shader stage changed semantic kind.");
            ShaderDescription description = Kind switch
            {
                RenderFragmentKind.Shader
                    => ((ShaderRenderFragmentPayload)reference.Payload!).Description,
                RenderFragmentKind.Opacity
                    => ((OpacityRenderFragmentPayload)reference.Payload!).FusionDescription,
                _ => throw new InvalidOperationException("A cached Shader run contains a non-Shader stage."),
            };
            return new CompiledShaderStage(
                graph.Fragments[Fragment].Id,
                reference,
                Kind,
                description,
                CoverageBehavior,
                ProgramStageIndex);
        }
    }

    private sealed record BoundaryTemplate(
        int? Before,
        int? After,
        ExecutionIslandBoundaryReason Reason,
        ImmutableArray<SkslBackendLimit> BackendLimits)
    {
        public static BoundaryTemplate Create(
            ExecutionIslandBoundary boundary,
            RecordedRenderGraph graph)
            => new(
                boundary.BeforeFragmentId is { } before ? GetFragmentIndex(before, graph) : null,
                boundary.AfterFragmentId is { } after ? GetFragmentIndex(after, graph) : null,
                boundary.Reason,
                boundary.BackendLimits);

        public ExecutionIslandBoundary Bind(RecordedRenderGraph graph)
            => new(
                Before is { } before ? graph.Fragments[before].Id : null,
                After is { } after ? graph.Fragments[after].Id : null,
                Reason,
                BackendLimits);
    }

    private static RenderFragmentId GetId(RenderFragmentReference reference)
        => reference.Id
           ?? throw new InvalidOperationException("A cached plan fragment has not been committed.");

    private static int GetFragmentIndex(RenderFragmentId id, RecordedRenderGraph graph)
    {
        if (id.RequestId != graph.RequestId || id.Value <= 0 || id.Value > graph.Fragments.Length)
            throw new InvalidOperationException("A cached plan fragment ID does not belong to its graph.");
        return checked((int)id.Value - 1);
    }
}
