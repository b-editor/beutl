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
        FusionMode fusionMode,
        SkslBackendBudget budget)
        => Plan(graph, roots, new RenderCacheResolution([]), fusionMode, budget);

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
        var referenceSet = new HashSet<RenderFragmentReference>(
            references,
            ReferenceEqualityComparer.Instance);
        foreach (RenderFragmentReference root in roots)
        {
            if (!referenceSet.Contains(root))
                throw new ArgumentException("A publication root is not part of the recorded graph.", nameof(roots));
        }

        Dictionary<RenderFragmentReference, int> consumerCounts = CountConsumers(
            references,
            roots,
            cacheResolution);
        var stageCandidates = new Dictionary<RenderFragmentReference, StageCandidate>(
            ReferenceEqualityComparer.Instance);
        var rejectedStageClassifications = new Dictionary<RenderFragmentReference, ExecutionIslandClassification>(
            ReferenceEqualityComparer.Instance);
        foreach (RenderFragmentReference reference in references)
        {
            if (cacheResolution.HasHitProducer(GetId(reference)))
                continue;

            if (TryCreateStage(reference, out StageCandidate? stage, out ExecutionIslandBoundaryReason reason))
            {
                StageCandidate accepted = stage!;
                if (fusionMode == FusionMode.Disabled && accepted.IsWholeSourceHeadOnly)
                {
                    rejectedStageClassifications.Add(
                        reference,
                        new ExecutionIslandClassification(
                            ExecutionIslandBoundaryReason.WholeSourceShader,
                            []));
                }
                else
                {
                    stageCandidates.Add(reference, accepted);
                }
            }
            else if (reference.Kind is RenderFragmentKind.Shader or RenderFragmentKind.Opacity)
            {
                rejectedStageClassifications.Add(
                    reference,
                    new ExecutionIslandClassification(reason, []));
            }
        }

        Dictionary<RenderFragmentReference, RenderFragmentReference> successors = BuildMergeableSuccessors(
            references,
            stageCandidates,
            consumerCounts,
            cacheResolution);
        var predecessors = new Dictionary<RenderFragmentReference, RenderFragmentReference>(
            ReferenceEqualityComparer.Instance);
        foreach ((RenderFragmentReference predecessor, RenderFragmentReference successor) in successors)
            predecessors.Add(successor, predecessor);

        var drafts = new List<IslandDraft>();
        var boundaries = new List<ExecutionIslandBoundary>();
        AddSelectedCacheBoundaries(
            references,
            cacheResolution,
            boundaries);
        var compiledFragments = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
        var visitedStages = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);

        foreach (RenderFragmentReference reference in references)
        {
            if (!stageCandidates.ContainsKey(reference)
                || predecessors.ContainsKey(reference)
                || visitedStages.Contains(reference))
            {
                continue;
            }

            List<StageCandidate> chain = BuildChain(
                reference,
                stageCandidates,
                successors,
                visitedStages);
            if (!chain.Any(static item => item.Fragment.Kind == RenderFragmentKind.Shader))
                continue;

            IReadOnlyList<ProgramGroup> groups = BuildProgramGroups(chain, fusionMode, budget);
            ProgramGroup? previous = null;
            foreach (ProgramGroup group in groups)
            {
                if (previous is not null)
                {
                    ExecutionIslandBoundaryReason splitReason = fusionMode == FusionMode.Disabled
                        ? ExecutionIslandBoundaryReason.FusionDisabled
                        : ExecutionIslandBoundaryReason.BackendLimit;
                    boundaries.Add(new ExecutionIslandBoundary(
                        GetFragmentIndex(previous.Stages[^1].Fragment),
                        GetFragmentIndex(group.Stages[0].Fragment),
                        splitReason,
                        splitReason == ExecutionIslandBoundaryReason.BackendLimit
                            ? GetSplitLimits(previous.Stages, group.Stages, budget)
                            : []));
                }
                else
                {
                    AddRunEntryBoundary(
                        chain[0],
                        stageCandidates,
                        consumerCounts,
                        cacheResolution,
                        boundaries);
                }

                if (group.Program.RequiresStandaloneExecution)
                {
                    StageCandidate standalone = group.Stages.Single();
                    rejectedStageClassifications[standalone.Fragment] = new ExecutionIslandClassification(
                        ExecutionIslandBoundaryReason.BackendLimit,
                        [.. group.Program.OverflowReasons]);
                    previous = group;
                    continue;
                }

                ImmutableArray<int> stageFragmentIndices =
                [.. group.Stages.Select(static item => GetFragmentIndex(item.Fragment))];
                if (group.Stages[0].Snippet.Description.Kind == ShaderDescriptionKind.WholeSource)
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
                previous = group;
            }
        }

        foreach (RenderFragmentReference reference in references)
        {
            if (compiledFragments.Contains(reference)
                || cacheResolution.HasHitProducer(GetId(reference))
                || reference.Kind is RenderFragmentKind.ContributeValues or RenderFragmentKind.MaterializedInput)
            {
                continue;
            }

            if (!TryClassifyExecutionIsland(
                    reference,
                    rejectedStageClassifications,
                    out ExecutionIslandClassification item))
                continue;

            bool requiresReadback = RequiresDeclaredReadback(reference);
            drafts.Add(new IslandDraft(
                GetId(reference).Value,
                [GetFragmentIndex(reference)],
                Program: null));
            boundaries.Add(new ExecutionIslandBoundary(
                reference.Inputs.IsDefaultOrEmpty ? null : GetFragmentIndex(reference.Inputs[0]),
                GetFragmentIndex(reference),
                item.Reason,
                item.BackendLimits));
            if (requiresReadback && item.Reason != ExecutionIslandBoundaryReason.Readback)
            {
                boundaries.Add(new ExecutionIslandBoundary(
                    reference.Inputs.IsDefaultOrEmpty ? null : GetFragmentIndex(reference.Inputs[0]),
                    GetFragmentIndex(reference),
                    ExecutionIslandBoundaryReason.Readback,
                    []));
            }
            if (item.Reason == ExecutionIslandBoundaryReason.ThreeD)
            {
                boundaries.Add(new ExecutionIslandBoundary(
                    reference.Inputs.IsDefaultOrEmpty ? null : GetFragmentIndex(reference.Inputs[0]),
                    GetFragmentIndex(reference),
                    ExecutionIslandBoundaryReason.BackendTransition,
                    []));
            }
        }

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

        ImmutableArray<ExecutionIslandBoundary> orderedBoundaries =
        [.. boundaries
            .Distinct(ExecutionIslandBoundaryComparer.Instance)
            .OrderBy(static item => item.AfterFragmentIndex ?? int.MinValue)
            .ThenBy(static item => item.BeforeFragmentIndex ?? int.MinValue)
            .ThenBy(static item => item.Reason)];
        return new ExecutionIslandPlan(graph.Fragments.Length, islands.MoveToImmutable(), orderedBoundaries);
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
            if (!stages.TryGetValue(current, out StageCandidate? currentStage)
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
            if (!visited.Add(current))
                throw new InvalidOperationException("The eligible Shader-stage graph contains a cycle.");
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
                SkslMergedProgram program = SkslSnippetMerger.MergeAndSplit([stage.Snippet], budget).Single();
                disabled.Add(new ProgramGroup([stage], program));
            }
            return disabled;
        }

        IReadOnlyList<SkslMergedProgram> programs = SkslSnippetMerger.MergeAndSplit(
            chain.SelectToArray(static item => item.Snippet),
            budget);
        var result = new List<ProgramGroup>(programs.Count);
        foreach (SkslMergedProgram program in programs)
        {
            StageCandidate[] stages = program.Stages
                .Select(layout => chain[layout.StageIndex])
                .ToArray();
            result.Add(new ProgramGroup(stages, program));
        }
        return result;
    }

    private static void AddRunEntryBoundary(
        StageCandidate first,
        IReadOnlyDictionary<RenderFragmentReference, StageCandidate> stages,
        IReadOnlyDictionary<RenderFragmentReference, int> consumerCounts,
        RenderCacheResolution cacheResolution,
        ICollection<ExecutionIslandBoundary> boundaries)
    {
        RenderFragmentReference input = first.Fragment.Inputs.Single();
        RenderFragmentId inputId = GetId(input);
        if (cacheResolution.HasHitProducer(inputId)
            || cacheResolution.HasMissCaptureProducer(inputId))
            return;

        ExecutionIslandBoundaryReason reason;
        if (stages.ContainsKey(input))
        {
            if (first.IsWholeSourceHeadOnly)
            {
                reason = ExecutionIslandBoundaryReason.WholeSourceShader;
            }
            else if (consumerCounts[input] != 1)
            {
                reason = ExecutionIslandBoundaryReason.Branching;
            }
            else if (!HasCompatibleMergeScale(input, first.Fragment))
            {
                reason = ExecutionIslandBoundaryReason.ScaleTransition;
            }
            else
            {
                throw new InvalidOperationException(
                    "A mergeable Shader-stage input cannot begin a separate execution chain.");
            }
        }
        else
        {
            reason = input.Kind == RenderFragmentKind.MaterializedInput
                ? ExecutionIslandBoundaryReason.MaterializedInput
                : ExecutionIslandBoundaryReason.CoverageResolution;
        }

        boundaries.Add(new ExecutionIslandBoundary(
            GetFragmentIndex(input),
            GetFragmentIndex(first.Fragment),
            reason,
            []));
    }

    private static void AddSelectedCacheBoundaries(
        IReadOnlyList<RenderFragmentReference> references,
        RenderCacheResolution cacheResolution,
        ICollection<ExecutionIslandBoundary> boundaries)
    {
        foreach (RenderFragmentReference reference in references)
        {
            RenderFragmentId producerId = GetId(reference);
            if (cacheResolution.HasHitProducer(producerId))
            {
                boundaries.Add(new ExecutionIslandBoundary(
                    BeforeFragmentIndex: null,
                    AfterFragmentIndex: GetFragmentIndex(producerId),
                    ExecutionIslandBoundaryReason.CacheInput,
                    []));
            }
            if (cacheResolution.HasMissCaptureProducer(producerId))
            {
                boundaries.Add(new ExecutionIslandBoundary(
                    BeforeFragmentIndex: GetFragmentIndex(producerId),
                    AfterFragmentIndex: null,
                    ExecutionIslandBoundaryReason.CacheCapture,
                    []));
            }
        }
    }

    private static bool TryCreateStage(
        RenderFragmentReference fragment,
        out StageCandidate? stage,
        out ExecutionIslandBoundaryReason rejectionReason)
    {
        stage = null;
        rejectionReason = ExecutionIslandBoundaryReason.UnsafeComposite;
        if (fragment.Kind is not (RenderFragmentKind.Shader or RenderFragmentKind.Opacity))
            return false;
        if (fragment.Inputs.Length != 1)
        {
            rejectionReason = ExecutionIslandBoundaryReason.DynamicTopology;
            return false;
        }
        RenderFragmentReference input = fragment.Inputs[0];
        bool isOptionalStage =
            fragment.ValueCardinality.Equals(RenderValueCardinality.ZeroOrOne)
            && input.ValueCardinality.Equals(RenderValueCardinality.ZeroOrOne);
        if (!fragment.ValueCardinality.Equals(RenderValueCardinality.Single)
            && !isOptionalStage)
        {
            rejectionReason = ExecutionIslandBoundaryReason.DynamicTopology;
            return false;
        }
        if (!fragment.CanBeUsedAsValueInput
            || !input.CanBeUsedAsValueInput
            || fragment.HasTargetEffects != input.HasTargetEffects
            || fragment.HasOpaqueExternalWork != input.HasOpaqueExternalWork)
        {
            rejectionReason = ExecutionIslandBoundaryReason.ScopeMismatch;
            return false;
        }

        ShaderDescription description;
        SkslCoverageBehavior coverageBehavior;
        if (fragment.Kind == RenderFragmentKind.Shader)
        {
            var payload = (ShaderRenderFragmentPayload?)fragment.Payload;
            if (payload is null)
            {
                rejectionReason = ExecutionIslandBoundaryReason.WholeSourceShader;
                return false;
            }
            description = payload.Description;
            coverageBehavior = SkslCoverageBehavior.RequiresResolvedCoverage;
        }
        else
        {
            var payload = (OpacityRenderFragmentPayload?)fragment.Payload;
            if (payload is null
                || payload.Opacity < 0
                || payload.Opacity > 1
                || !HasCompatibleOpacityFusionMetadata(input, fragment))
            {
                rejectionReason = ExecutionIslandBoundaryReason.UnsafeComposite;
                return false;
            }
            description = payload.FusionDescription;
            coverageBehavior = SkslCoverageBehavior.PremultipliedCoverageHomogeneous;
        }

        stage = new StageCandidate(
            fragment,
            new SkslSnippetStage(description, coverageBehavior),
            description.Kind == ShaderDescriptionKind.WholeSource);
        return true;
    }

    // A segment also collects Skia items and typed suffixes, so a custom effect is only the reason
    // the island cannot fuse when the segment actually contains one.
    private static ExecutionIslandBoundaryReason SegmentBoundaryReason(
        FilterEffectSegmentRenderFragmentPayload payload)
        => payload.HasImperativeItem
            ? ExecutionIslandBoundaryReason.CustomEffectItem
            : ExecutionIslandBoundaryReason.FilterEffectSegment;

    private static bool TryClassifyExecutionIsland(
        RenderFragmentReference reference,
        IReadOnlyDictionary<RenderFragmentReference, ExecutionIslandClassification> rejectedStageClassifications,
        out ExecutionIslandClassification result)
    {
        if (rejectedStageClassifications.TryGetValue(reference, out result))
            return true;

        result = reference.Kind switch
        {
            RenderFragmentKind.Opacity => new(ExecutionIslandBoundaryReason.SemanticComposite, []),
            RenderFragmentKind.Shader => new(ExecutionIslandBoundaryReason.WholeSourceShader, []),
            RenderFragmentKind.Geometry => new(ExecutionIslandBoundaryReason.Geometry, []),
            RenderFragmentKind.OpaqueSource
                or RenderFragmentKind.OpaqueMap
                or RenderFragmentKind.OpaqueCombine
                or RenderFragmentKind.OpaqueExpand
                when reference.Payload is OpaqueRenderFragmentPayload opaque
                     && opaque.Description.BackendBoundary
                     == RenderBackendBoundary.Graphics3D => new(ExecutionIslandBoundaryReason.ThreeD, []),
            RenderFragmentKind.OpaqueSource
                or RenderFragmentKind.OpaqueMap
                or RenderFragmentKind.OpaqueCombine
                or RenderFragmentKind.OpaqueExpand => new(ExecutionIslandBoundaryReason.Opaque, []),
            RenderFragmentKind.FilterEffectSegment => new(
                SegmentBoundaryReason((FilterEffectSegmentRenderFragmentPayload)reference.Payload!),
                []),
            RenderFragmentKind.TargetCapture
                or RenderFragmentKind.BuiltInBackdropCapture => new(
                    ExecutionIslandBoundaryReason.TargetCapture, []),
            RenderFragmentKind.Layer => new(ExecutionIslandBoundaryReason.Layer, []),
            RenderFragmentKind.TargetLayerScope
                or RenderFragmentKind.TargetScope => new(ExecutionIslandBoundaryReason.TargetScope, []),
            RenderFragmentKind.RawTargetScope
                or RenderFragmentKind.RawTargetCommand => new(ExecutionIslandBoundaryReason.RawCanvas, []),
            RenderFragmentKind.TargetCommand
                when ((TargetCommandRenderFragmentPayload)reference.Payload!).Description.Access
                     == TargetAccess.Readback => new(ExecutionIslandBoundaryReason.Readback, []),
            RenderFragmentKind.TargetCommand => new(ExecutionIslandBoundaryReason.TargetCommand, []),
            RenderFragmentKind.Blend
                or RenderFragmentKind.OpacityMask => new(ExecutionIslandBoundaryReason.UnsafeComposite, []),
            _ => default,
        };
        return result != default;
    }

    private static bool RequiresDeclaredReadback(RenderFragmentReference reference)
        => reference.Payload switch
        {
            GeometryRenderFragmentPayload geometry => geometry.Description.RequiresReadback,
            OpaqueRenderFragmentPayload opaque
                => opaque.InputReadbacks.Any(static item => item.RequiresAnyReadback),
            TargetCommandRenderFragmentPayload command
                => command.Description.Access == TargetAccess.Readback
                   || command.InputReadbacks.Any(static item => item.RequiresAnyReadback),
            _ => false,
        };

    private static ImmutableArray<SkslBackendLimit> GetSplitLimits(
        IReadOnlyList<StageCandidate> previous,
        IReadOnlyList<StageCandidate> current,
        SkslBackendBudget budget)
    {
        SkslMergedProgram combined = SkslSnippetMerger.Merge(
            previous.Concat(current.Take(1)).Select(static item => item.Snippet).ToArray());
        var result = ImmutableArray.CreateBuilder<SkslBackendLimit>();
        if (combined.StageCount > budget.MaxStages)
            result.Add(SkslBackendLimit.StageCount);
        if (combined.UniformVectorCount > budget.MaxUniformVectors)
            result.Add(SkslBackendLimit.UniformVectors);
        if (combined.SamplerCount > budget.MaxSamplers)
            result.Add(SkslBackendLimit.Samplers);
        if (combined.ChildCount > budget.MaxChildren)
            result.Add(SkslBackendLimit.Children);
        if (combined.SourceByteCount > budget.MaxSourceBytes)
            result.Add(SkslBackendLimit.SourceBytes);
        if (combined.ProgramTokenCount > budget.MaxProgramTokens)
            result.Add(SkslBackendLimit.ProgramTokens);
        return result.ToImmutable();
    }

    private static RenderFragmentId GetId(RenderFragmentReference reference)
        => reference.Id
           ?? throw new InvalidOperationException("An execution-planner fragment is not committed.");

    private static int GetFragmentIndex(RenderFragmentReference reference)
        => GetFragmentIndex(GetId(reference));

    private static int GetFragmentIndex(RenderFragmentId id)
        => checked((int)id.Value - 1);

    private sealed record StageCandidate(
        RenderFragmentReference Fragment,
        SkslSnippetStage Snippet,
        bool IsWholeSourceHeadOnly);

    private sealed record ProgramGroup(
        IReadOnlyList<StageCandidate> Stages,
        SkslMergedProgram Program);

    private sealed record IslandDraft(
        long AuthoredOrder,
        ImmutableArray<int> FragmentIndices,
        SkslMergedProgram? Program);

    private readonly record struct ExecutionIslandClassification(
        ExecutionIslandBoundaryReason Reason,
        ImmutableArray<SkslBackendLimit> BackendLimits);

    private sealed class ExecutionIslandBoundaryComparer : IEqualityComparer<ExecutionIslandBoundary>
    {
        public static ExecutionIslandBoundaryComparer Instance { get; } = new();

        public bool Equals(ExecutionIslandBoundary x, ExecutionIslandBoundary y)
            => x.BeforeFragmentIndex == y.BeforeFragmentIndex
               && x.AfterFragmentIndex == y.AfterFragmentIndex
               && x.Reason == y.Reason
               && x.BackendLimits.AsSpan().SequenceEqual(y.BackendLimits.AsSpan());

        public int GetHashCode(ExecutionIslandBoundary obj)
        {
            var hash = new HashCode();
            hash.Add(obj.BeforeFragmentIndex);
            hash.Add(obj.AfterFragmentIndex);
            hash.Add(obj.Reason);
            foreach (SkslBackendLimit limit in obj.BackendLimits)
                hash.Add(limit);
            return hash.ToHashCode();
        }
    }
}
