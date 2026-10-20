using System.Collections.Immutable;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Rendering.Requests;

/// <summary>
/// Partitions the recorded fragment DAG without executing it. Shader runs are restricted to direct, at-most-one-output,
/// target-independent chains so merging cannot change fan-out, painter order, group opacity, or target-token scope.
/// </summary>
internal sealed class ExecutionIslandPlanner
{
    internal static bool HasCompatibleMergeScale(
        RenderFragmentReference predecessor,
        RenderFragmentReference successor)
        => predecessor.EffectiveScale.IsUnbounded
           || predecessor.EffectiveScale == successor.EffectiveScale;

    internal static bool HasCompatibleOpacityFusionMetadata(
        RenderFragmentReference input,
        RenderFragmentReference opacity)
        => input.Bounds == opacity.Bounds
           && input.EffectiveScale == opacity.EffectiveScale;

    public ExecutionIslandPlan Plan(
        RecordedRenderGraph graph,
        ImmutableArray<RenderFragmentReference> roots,
        RenderCacheResolution cacheResolution,
        FusionMode fusionMode,
        SkslBackendBudget budget)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(cacheResolution);
        if (roots.IsDefault)
            throw new ArgumentException("Publication roots must be initialized.", nameof(roots));
        if (!Enum.IsDefined(fusionMode))
            throw new ArgumentOutOfRangeException(nameof(fusionMode));
        ArgumentNullException.ThrowIfNull(budget);

        RenderFragmentReference[] references = GetOrderedReferences(graph, roots, cacheResolution);

        Dictionary<RenderFragmentReference, int> consumerCounts = CountConsumers(
            references,
            roots,
            cacheResolution);
        Dictionary<RenderFragmentReference, StageCandidate> stageCandidates = ClassifyStageCandidates(
            references,
            cacheResolution,
            fusionMode);

        Dictionary<RenderFragmentReference, RenderFragmentReference> successors = BuildMergeableSuccessors(
            references,
            stageCandidates,
            consumerCounts,
            cacheResolution);
        var stages = new StageGraph(stageCandidates, successors, InvertSuccessors(successors));

        var drafts = new List<IslandDraft>();
        HashSet<RenderFragmentReference> compiledFragments = AddShaderRunDrafts(
            references,
            stages,
            fusionMode,
            budget,
            drafts);
        AddStandaloneIslandDrafts(
            references,
            compiledFragments,
            cacheResolution,
            drafts);
        return AssemblePlan(graph, drafts);
    }

    private static Dictionary<RenderFragmentReference, StageCandidate> ClassifyStageCandidates(
        RenderFragmentReference[] references,
        RenderCacheResolution cacheResolution,
        FusionMode fusionMode)
    {
        var stageCandidates = new Dictionary<RenderFragmentReference, StageCandidate>(
            ReferenceEqualityComparer.Instance);
        foreach (RenderFragmentReference reference in references)
        {
            if (cacheResolution.HasHitProducer(GetId(reference)))
                continue;

            if (TryCreateStage(reference, out StageCandidate stage)
                && !(fusionMode == FusionMode.Disabled && stage.IsWholeSourceHeadOnly))
            {
                stageCandidates.Add(reference, stage);
            }
        }

        return stageCandidates;
    }

    private static Dictionary<RenderFragmentReference, RenderFragmentReference> InvertSuccessors(
        Dictionary<RenderFragmentReference, RenderFragmentReference> successors)
    {
        var predecessors = new Dictionary<RenderFragmentReference, RenderFragmentReference>(
            ReferenceEqualityComparer.Instance);
        foreach ((RenderFragmentReference predecessor, RenderFragmentReference successor) in successors)
            predecessors.Add(successor, predecessor);
        return predecessors;
    }

    private static HashSet<RenderFragmentReference> AddShaderRunDrafts(
        RenderFragmentReference[] references,
        StageGraph stages,
        FusionMode fusionMode,
        SkslBackendBudget budget,
        List<IslandDraft> drafts)
    {
        var compiledFragments = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
        var visitedStages = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);

        foreach (RenderFragmentReference reference in references)
        {
            if (!stages.Candidates.ContainsKey(reference)
                || stages.Predecessors.ContainsKey(reference)
                || visitedStages.Contains(reference))
            {
                continue;
            }

            List<StageCandidate> chain = BuildChain(
                reference,
                stages.Candidates,
                stages.Successors,
                visitedStages);
            if (!chain.Any(static item => item.Fragment.Kind == RenderFragmentKind.Shader))
                continue;

            foreach (ProgramGroup group in BuildProgramGroups(chain, fusionMode, budget))
            {
                // A stage that overflows the budget on its own runs as a standalone island instead.
                if (group.Program.RequiresStandaloneExecution)
                    continue;

                ImmutableArray<int> stageFragmentIndices =
                [.. group.Stages.Select(static item => GetFragmentIndex(item.Fragment))];
                if (group.Stages[0].Description.Kind == ShaderDescriptionKind.WholeSource)
                {
                    RenderFragmentReference head = group.Stages[0].Fragment;
                    RenderFragmentReference output = group.Stages[^1].Fragment;
                    if (output.Bounds != head.Bounds || output.EffectiveScale != head.EffectiveScale)
                    {
                        throw new InvalidOperationException(
                            "A WholeSource-headed run must preserve the head stage's output bounds and effective scale.");
                    }
                }
                drafts.Add(new IslandDraft(
                    GetId(group.Stages[0].Fragment).Value,
                    stageFragmentIndices,
                    group.Program));
                foreach (StageCandidate stage in group.Stages)
                    compiledFragments.Add(stage.Fragment);
            }
        }

        return compiledFragments;
    }

    private static void AddStandaloneIslandDrafts(
        RenderFragmentReference[] references,
        HashSet<RenderFragmentReference> compiledFragments,
        RenderCacheResolution cacheResolution,
        List<IslandDraft> drafts)
    {
        foreach (RenderFragmentReference reference in references)
        {
            if (compiledFragments.Contains(reference)
                || cacheResolution.HasHitProducer(GetId(reference))
                || reference.Kind is RenderFragmentKind.ContributeValues or RenderFragmentKind.MaterializedInput)
            {
                continue;
            }

            drafts.Add(new IslandDraft(
                GetId(reference).Value,
                [GetFragmentIndex(reference)],
                Program: null));
        }
    }

    private static ExecutionIslandPlan AssemblePlan(
        RecordedRenderGraph graph,
        List<IslandDraft> drafts)
    {
        IslandDraft[] orderedDrafts = [.. drafts.OrderBy(static item => item.AuthoredOrder)];
        var islands = ImmutableArray.CreateBuilder<ExecutionIsland>(orderedDrafts.Length);
        for (int index = 0; index < orderedDrafts.Length; index++)
        {
            IslandDraft draft = orderedDrafts[index];
            CompiledShaderRun? run = draft.Program is not null
                ? new CompiledShaderRun(draft.FragmentIndices, draft.Program)
                : null;

            islands.Add(new ExecutionIsland(
                index,
                draft.FragmentIndices,
                run));
        }

        return new ExecutionIslandPlan(graph.Fragments.Length, islands.MoveToImmutable());
    }

    private static RenderFragmentReference[] GetOrderedReferences(
        RecordedRenderGraph graph,
        ImmutableArray<RenderFragmentReference> roots,
        RenderCacheResolution cacheResolution)
    {
        foreach (RenderFragmentReference root in roots)
        {
            if (root.Id is not { } id
                || id.RequestId != graph.RequestId
                || id.Value <= 0
                || id.Value > graph.Fragments.Length
                || !ReferenceEquals(graph.Fragments[checked((int)id.Value - 1)], root))
            {
                throw new ArgumentException("A publication root is not part of the recorded graph.", nameof(roots));
            }
        }

        var reachable = new HashSet<RenderFragmentReference>(
            roots,
            ReferenceEqualityComparer.Instance);
        int reachableCount = 0;
        for (int index = graph.Fragments.Length - 1; index >= 0; index--)
        {
            RenderFragmentReference reference = graph.GetFragment(
                new RenderFragmentId(graph.RequestId, index + 1L));
            if (!reachable.Contains(reference))
                continue;
            reachableCount++;
            if (cacheResolution.HasHitProducer(GetId(reference)))
                continue;
            foreach (RenderFragmentReference input in reference.ExecutionInputs)
                reachable.Add(input);
        }

        var ordered = new RenderFragmentReference[reachableCount];
        int next = 0;
        foreach (RenderFragmentReference reference in graph.Fragments)
        {
            if (reachable.Contains(reference))
                ordered[next++] = reference;
        }

        return ordered;
    }

    private static Dictionary<RenderFragmentReference, int> CountConsumers(
        IReadOnlyList<RenderFragmentReference> references,
        ImmutableArray<RenderFragmentReference> roots,
        RenderCacheResolution cacheResolution)
    {
        var result = new Dictionary<RenderFragmentReference, int>(ReferenceEqualityComparer.Instance);
        foreach (RenderFragmentReference reference in references)
            result.Add(reference, 0);

        foreach (RenderFragmentReference reference in references)
        {
            if (cacheResolution.HasHitProducer(GetId(reference)))
                continue;
            foreach (RenderFragmentReference input in reference.ExecutionInputs)
            {
                if (!result.TryGetValue(input, out int count))
                {
                    throw new InvalidOperationException(
                        "An execution-planner input is not part of the recorded request graph.");
                }
                result[input] = checked(count + 1);
            }
        }

        foreach (RenderFragmentReference root in roots)
            result[root] = checked(result[root] + 1);
        return result;
    }

    private static Dictionary<RenderFragmentReference, RenderFragmentReference> BuildMergeableSuccessors(
        IReadOnlyList<RenderFragmentReference> references,
        IReadOnlyDictionary<RenderFragmentReference, StageCandidate> stages,
        IReadOnlyDictionary<RenderFragmentReference, int> consumerCounts,
        RenderCacheResolution cacheResolution)
    {
        var candidates = new Dictionary<RenderFragmentReference, List<RenderFragmentReference>>(
            ReferenceEqualityComparer.Instance);
        foreach (RenderFragmentReference current in references)
        {
            if (!stages.TryGetValue(current, out StageCandidate currentStage)
                || currentStage.IsWholeSourceHeadOnly
                || current.Inputs.Length != 1)
                continue;

            RenderFragmentReference input = current.Inputs[0];
            if (!stages.ContainsKey(input))
                continue;

            if (!candidates.TryGetValue(input, out List<RenderFragmentReference>? values))
            {
                values = [];
                candidates.Add(input, values);
            }
            values.Add(current);
        }

        var result = new Dictionary<RenderFragmentReference, RenderFragmentReference>(
            ReferenceEqualityComparer.Instance);
        foreach ((RenderFragmentReference predecessor, List<RenderFragmentReference> values) in candidates)
        {
            if (values.Count != 1
                || consumerCounts[predecessor] != 1
                || cacheResolution.HasMissCaptureProducer(GetId(predecessor))
                || !HasCompatibleMergeScale(predecessor, values[0]))
            {
                continue;
            }

            result.Add(predecessor, values[0]);
        }
        return result;
    }

    private static List<StageCandidate> BuildChain(
        RenderFragmentReference first,
        IReadOnlyDictionary<RenderFragmentReference, StageCandidate> stages,
        IReadOnlyDictionary<RenderFragmentReference, RenderFragmentReference> successors,
        ISet<RenderFragmentReference> visited)
    {
        var result = new List<StageCandidate>();
        RenderFragmentReference? current = first;
        while (current is not null)
        {
            visited.Add(current);
            result.Add(stages[current]);
            current = successors.TryGetValue(current, out RenderFragmentReference? next) ? next : null;
        }
        return result;
    }

    private static IReadOnlyList<ProgramGroup> BuildProgramGroups(
        IReadOnlyList<StageCandidate> chain,
        FusionMode fusionMode,
        SkslBackendBudget budget)
    {
        if (fusionMode == FusionMode.Disabled)
        {
            var disabled = new List<ProgramGroup>(chain.Count);
            foreach (StageCandidate stage in chain)
            {
                SkslMergedProgram program = SkslSnippetMerger.MergeAndSplit([stage.Description], budget).Single();
                disabled.Add(new ProgramGroup([stage], program));
            }
            return disabled;
        }

        IReadOnlyList<SkslMergedProgram> programs = SkslSnippetMerger.MergeAndSplit(
            chain.SelectToArray(static item => item.Description),
            budget);
        var result = new List<ProgramGroup>(programs.Count);
        foreach (SkslMergedProgram program in programs)
        {
            var stages = new StageCandidate[program.StageCount];
            for (int index = 0; index < stages.Length; index++)
                stages[index] = chain[program.FirstStageIndex + index];
            result.Add(new ProgramGroup(stages, program));
        }
        return result;
    }

    private static bool TryCreateStage(
        RenderFragmentReference fragment,
        out StageCandidate stage)
    {
        stage = default;
        if (fragment.Kind is not (RenderFragmentKind.Shader or RenderFragmentKind.Opacity))
            return false;
        if (fragment.Inputs.Length != 1)
            return false;
        RenderFragmentReference input = fragment.Inputs[0];
        bool isOptionalStage =
            fragment.ValueCardinality.Equals(RenderValueCardinality.ZeroOrOne)
            && input.ValueCardinality.Equals(RenderValueCardinality.ZeroOrOne);
        if (!fragment.ValueCardinality.Equals(RenderValueCardinality.Single)
            && !isOptionalStage)
        {
            return false;
        }
        if (!fragment.CanBeUsedAsValueInput
            || !input.CanBeUsedAsValueInput
            || fragment.HasTargetEffects != input.HasTargetEffects
            || fragment.HasOpaqueExternalWork != input.HasOpaqueExternalWork)
        {
            return false;
        }

        ShaderDescription description;
        if (fragment.Kind == RenderFragmentKind.Shader)
        {
            var payload = (ShaderRenderFragmentPayload?)fragment.Payload;
            if (payload is null)
                return false;
            description = payload.Description;
        }
        else
        {
            var payload = (OpacityRenderFragmentPayload?)fragment.Payload;
            if (payload is null
                || payload.Opacity < 0
                || payload.Opacity > 1
                || !HasCompatibleOpacityFusionMetadata(input, fragment))
            {
                return false;
            }
            description = payload.FusionDescription;
        }

        stage = new StageCandidate(fragment, description);
        return true;
    }

    private static RenderFragmentId GetId(RenderFragmentReference reference)
        => reference.Id
           ?? throw new InvalidOperationException("An execution-planner fragment is not committed.");

    private static int GetFragmentIndex(RenderFragmentReference reference)
        => GetFragmentIndex(GetId(reference));

    private static int GetFragmentIndex(RenderFragmentId id)
        => checked((int)id.Value - 1);

    private readonly record struct StageCandidate(
        RenderFragmentReference Fragment,
        ShaderDescription Description)
    {
        public bool IsWholeSourceHeadOnly => Description.Kind == ShaderDescriptionKind.WholeSource;
    }

    // The read-only stage maps the shader-run pass walks.
    private readonly record struct StageGraph(
        Dictionary<RenderFragmentReference, StageCandidate> Candidates,
        Dictionary<RenderFragmentReference, RenderFragmentReference> Successors,
        Dictionary<RenderFragmentReference, RenderFragmentReference> Predecessors);

    private readonly record struct ProgramGroup(
        IReadOnlyList<StageCandidate> Stages,
        SkslMergedProgram Program);

    private readonly record struct IslandDraft(
        long AuthoredOrder,
        ImmutableArray<int> FragmentIndices,
        SkslMergedProgram? Program);
}
