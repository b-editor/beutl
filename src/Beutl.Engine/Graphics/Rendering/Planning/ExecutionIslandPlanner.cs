using System.Collections.Immutable;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Partitions the recorded value DAG without executing it. Shader runs are restricted to direct, single-value,
/// target-independent chains so merging cannot change fan-out, painter order, group opacity, or target-token scope.
/// </summary>
internal sealed class ExecutionIslandPlanner
{
    internal static bool HasCompatibleMergeScale(
        RenderFragmentReference predecessor,
        RenderFragmentReference successor)
        => predecessor.EffectiveScale.IsUnbounded
           || predecessor.EffectiveScale == successor.EffectiveScale;

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

        HashSet<RenderFragmentId> cacheHitIds =
        [
            .. cacheResolution.Hits.Select(static hit => hit.OriginalProducerId),
        ];
        HashSet<RenderFragmentId> cacheCaptureIds =
        [
            .. cacheResolution.MissCaptures.Select(static capture => capture.ProducerId),
        ];
        RenderFragmentReference[] references = GetOrderedReferences(graph, roots, cacheHitIds);
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
            cacheHitIds);
        var stageCandidates = new Dictionary<RenderFragmentReference, StageCandidate>(
            ReferenceEqualityComparer.Instance);
        var rejectedStageClassifications = new Dictionary<RenderFragmentReference, CompatibilityClassification>(
            ReferenceEqualityComparer.Instance);
        foreach (RenderFragmentReference reference in references)
        {
            if (cacheHitIds.Contains(GetId(reference)))
                continue;

            if (TryCreateStage(reference, out StageCandidate? stage, out ExecutionIslandBoundaryReason reason))
                stageCandidates.Add(reference, stage!);
            else if (reference.Kind is RenderFragmentKind.Shader or RenderFragmentKind.Opacity)
            {
                rejectedStageClassifications.Add(
                    reference,
                    new CompatibilityClassification(ExecutionIslandKind.Compatibility, reason, []));
            }
        }

        Dictionary<RenderFragmentReference, RenderFragmentReference> successors = BuildMergeableSuccessors(
            references,
            stageCandidates,
            consumerCounts,
            cacheCaptureIds);
        var predecessors = new Dictionary<RenderFragmentReference, RenderFragmentReference>(
            ReferenceEqualityComparer.Instance);
        foreach ((RenderFragmentReference predecessor, RenderFragmentReference successor) in successors)
            predecessors.Add(successor, predecessor);

        var drafts = new List<IslandDraft>();
        var boundaries = new List<ExecutionIslandBoundary>();
        AddSelectedCacheBoundaries(
            references,
            cacheHitIds,
            cacheCaptureIds,
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
                        GetId(previous.Stages[^1].Fragment),
                        GetId(group.Stages[0].Fragment),
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
                        cacheHitIds,
                        cacheCaptureIds,
                        boundaries);
                }

                if (group.Program.RequiresStandaloneExecution)
                {
                    StageCandidate standalone = group.Stages.Single();
                    rejectedStageClassifications[standalone.Fragment] = new CompatibilityClassification(
                        ExecutionIslandKind.Compatibility,
                        ExecutionIslandBoundaryReason.BackendLimit,
                        [.. group.Program.OverflowReasons]);
                    previous = group;
                    continue;
                }

                RenderFragmentReference input = group.Stages[0].Fragment.Inputs.Single();
                RenderFragmentReference output = group.Stages[^1].Fragment;
                ShaderRunCoverageSource coverageSource = ResolveCoverageSource(
                    input,
                    compiledFragments,
                    group.Stages,
                    cacheHitIds);
                drafts.Add(new IslandDraft(
                    GetId(group.Stages[0].Fragment).Value,
                    ExecutionIslandKind.ShaderRun,
                    [.. group.Stages.Select(static item => GetId(item.Fragment))],
                    PlansGpuPass: true,
                    input,
                    output,
                    CreateCompiledStages(group),
                    group.Program,
                    coverageSource));
                foreach (StageCandidate stage in group.Stages)
                    compiledFragments.Add(stage.Fragment);
                previous = group;
            }
        }

        foreach (RenderFragmentReference reference in references)
        {
            if (compiledFragments.Contains(reference)
                || cacheHitIds.Contains(GetId(reference))
                || reference.Kind is RenderFragmentKind.ContributeValues or RenderFragmentKind.MaterializedInput)
            {
                continue;
            }

            if (!TryClassifyCompatibility(
                    reference,
                    rejectedStageClassifications,
                    out CompatibilityClassification item))
                continue;

            bool requiresReadback = RequiresDeclaredReadback(reference);
            if (requiresReadback)
                item = item with { Kind = ExecutionIslandKind.Readback };

            drafts.Add(new IslandDraft(
                GetId(reference).Value,
                item.Kind,
                [GetId(reference)],
                PlansGpuPass: PlansGpuPass(reference),
                Input: null,
                Output: null,
                Stages: [],
                Program: null,
                ShaderRunCoverageSource.CompatibilityMaterialization));
            boundaries.Add(new ExecutionIslandBoundary(
                reference.Inputs.IsDefaultOrEmpty ? null : GetId(reference.Inputs[0]),
                GetId(reference),
                item.Reason,
                item.BackendLimits));
            if (requiresReadback && item.Reason != ExecutionIslandBoundaryReason.Readback)
            {
                boundaries.Add(new ExecutionIslandBoundary(
                    reference.Inputs.IsDefaultOrEmpty ? null : GetId(reference.Inputs[0]),
                    GetId(reference),
                    ExecutionIslandBoundaryReason.Readback,
                    []));
            }
            if (item.Reason == ExecutionIslandBoundaryReason.ThreeD)
            {
                boundaries.Add(new ExecutionIslandBoundary(
                    reference.Inputs.IsDefaultOrEmpty ? null : GetId(reference.Inputs[0]),
                    GetId(reference),
                    ExecutionIslandBoundaryReason.BackendTransition,
                    []));
            }
        }

        IslandDraft[] orderedDrafts = [.. drafts
            .OrderBy(static item => item.AuthoredOrder)
            .ThenBy(static item => item.Kind)];
        var islands = ImmutableArray.CreateBuilder<ExecutionIsland>(orderedDrafts.Length);
        int nextRunId = 0;
        for (int index = 0; index < orderedDrafts.Length; index++)
        {
            IslandDraft draft = orderedDrafts[index];
            CompiledShaderRun? run = null;
            if (draft.Kind == ExecutionIslandKind.ShaderRun)
            {
                run = new CompiledShaderRun(
                    new CompiledShaderRunId(++nextRunId),
                    draft.Input!,
                    draft.Output!,
                    draft.Stages,
                    draft.Program!,
                    draft.CoverageSource);
            }

            islands.Add(new ExecutionIsland(
                new ExecutionIslandId(index + 1),
                draft.Kind,
                draft.Fragments,
                draft.PlansGpuPass,
                run));
        }

        ImmutableArray<ExecutionIslandBoundary> orderedBoundaries =
        [.. boundaries
            .Distinct(ExecutionIslandBoundaryComparer.Instance)
            .OrderBy(static item => item.AfterFragmentId?.Value ?? long.MinValue)
            .ThenBy(static item => item.BeforeFragmentId?.Value ?? long.MinValue)
            .ThenBy(static item => item.Reason)];
        return new ExecutionIslandPlan(islands.MoveToImmutable(), orderedBoundaries);
    }

    private static RenderFragmentReference[] GetOrderedReferences(
        RecordedRenderGraph graph,
        ImmutableArray<RenderFragmentReference> roots,
        IReadOnlySet<RenderFragmentId> cacheHitIds)
    {
        var ordered = new RenderFragmentReference[graph.Fragments.Length];
        var all = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
        for (int index = 0; index < graph.Fragments.Length; index++)
        {
            RecordedRenderFragment recorded = graph.Fragments[index];
            if (recorded.Payload is not RenderFragmentReference reference)
            {
                throw new InvalidOperationException(
                    "A recorded fragment is missing its planner-visible semantic reference.");
            }
            if (reference.Id != recorded.Id)
                throw new InvalidOperationException("A recorded fragment reference has a mismatched request ID.");
            ordered[index] = reference;
            all.Add(reference);
        }

        var reachable = new HashSet<RenderFragmentReference>(
            roots,
            ReferenceEqualityComparer.Instance);
        if (!reachable.IsSubsetOf(all))
            throw new ArgumentException("A publication root is not part of the recorded graph.", nameof(roots));
        for (int index = ordered.Length - 1; index >= 0; index--)
        {
            RenderFragmentReference reference = ordered[index];
            if (!reachable.Contains(reference))
                continue;
            if (cacheHitIds.Contains(GetId(reference)))
                continue;

            foreach (RenderFragmentReference input in reference.Inputs)
                reachable.Add(input);
        }

        return [.. ordered.Where(reachable.Contains)];
    }

    private static Dictionary<RenderFragmentReference, int> CountConsumers(
        IReadOnlyList<RenderFragmentReference> references,
        ImmutableArray<RenderFragmentReference> roots,
        IReadOnlySet<RenderFragmentId> cacheHitIds)
    {
        var result = new Dictionary<RenderFragmentReference, int>(ReferenceEqualityComparer.Instance);
        foreach (RenderFragmentReference reference in references)
            result.Add(reference, 0);

        foreach (RenderFragmentReference reference in references)
        {
            if (cacheHitIds.Contains(GetId(reference)))
                continue;

            foreach (RenderFragmentReference input in reference.Inputs)
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
        IReadOnlySet<RenderFragmentId> cacheCaptureIds)
    {
        var candidates = new Dictionary<RenderFragmentReference, List<RenderFragmentReference>>(
            ReferenceEqualityComparer.Instance);
        foreach (RenderFragmentReference current in references)
        {
            if (!stages.ContainsKey(current) || current.Inputs.Length != 1)
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
                || cacheCaptureIds.Contains(GetId(predecessor))
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
            chain.Select(static item => item.Snippet).ToArray(),
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
        IReadOnlySet<RenderFragmentId> cacheHitIds,
        IReadOnlySet<RenderFragmentId> cacheCaptureIds,
        ICollection<ExecutionIslandBoundary> boundaries)
    {
        RenderFragmentReference input = first.Fragment.Inputs.Single();
        RenderFragmentId inputId = GetId(input);
        if (cacheHitIds.Contains(inputId) || cacheCaptureIds.Contains(inputId))
            return;

        ExecutionIslandBoundaryReason reason;
        if (stages.ContainsKey(input))
        {
            reason = consumerCounts[input] != 1
                ? ExecutionIslandBoundaryReason.Branching
                : !HasCompatibleMergeScale(input, first.Fragment)
                    ? ExecutionIslandBoundaryReason.ScaleTransition
                    : ExecutionIslandBoundaryReason.ScopeMismatch;
        }
        else
        {
            reason = input.Kind == RenderFragmentKind.MaterializedInput
                ? ExecutionIslandBoundaryReason.MaterializedInput
                : ExecutionIslandBoundaryReason.CoverageResolution;
        }

        boundaries.Add(new ExecutionIslandBoundary(
            GetId(input),
            GetId(first.Fragment),
            reason,
            []));
    }

    private static ShaderRunCoverageSource ResolveCoverageSource(
        RenderFragmentReference input,
        IReadOnlySet<RenderFragmentReference> compiledFragments,
        IReadOnlyList<StageCandidate> stages,
        IReadOnlySet<RenderFragmentId> cacheHitIds)
    {
        if (compiledFragments.Contains(input))
            return ShaderRunCoverageSource.PriorShaderRun;
        if (input.Kind == RenderFragmentKind.MaterializedInput
            || cacheHitIds.Contains(GetId(input)))
            return ShaderRunCoverageSource.MaterializedInput;
        if (stages.All(static item =>
                item.Snippet.CoverageBehavior == SkslCoverageBehavior.PremultipliedCoverageHomogeneous))
        {
            return ShaderRunCoverageSource.EngineHomogeneousProof;
        }
        return ShaderRunCoverageSource.CompatibilityMaterialization;
    }

    private static void AddSelectedCacheBoundaries(
        IReadOnlyList<RenderFragmentReference> references,
        IReadOnlySet<RenderFragmentId> cacheHitIds,
        IReadOnlySet<RenderFragmentId> cacheCaptureIds,
        ICollection<ExecutionIslandBoundary> boundaries)
    {
        HashSet<RenderFragmentId> reachableIds =
        [
            .. references.Select(GetId),
        ];
        foreach (RenderFragmentId hitId in cacheHitIds)
        {
            if (reachableIds.Contains(hitId))
            {
                boundaries.Add(new ExecutionIslandBoundary(
                    BeforeFragmentId: null,
                    AfterFragmentId: hitId,
                    ExecutionIslandBoundaryReason.CacheInput,
                    []));
            }
        }

        foreach (RenderFragmentId captureId in cacheCaptureIds)
        {
            if (reachableIds.Contains(captureId))
            {
                boundaries.Add(new ExecutionIslandBoundary(
                    BeforeFragmentId: captureId,
                    AfterFragmentId: null,
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
        if (fragment.Inputs.Length != 1
            || !fragment.ValueCardinality.Equals(RenderValueCardinality.Single))
        {
            rejectionReason = ExecutionIslandBoundaryReason.DynamicTopology;
            return false;
        }
        RenderFragmentReference input = fragment.Inputs[0];
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
            if (payload?.Description.Kind != ShaderDescriptionKind.CurrentPixel)
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
                || fragment.Bounds != fragment.Inputs[0].Bounds
                || fragment.EffectiveScale != fragment.Inputs[0].EffectiveScale)
            {
                rejectionReason = ExecutionIslandBoundaryReason.UnsafeComposite;
                return false;
            }
            description = payload.FusionDescription;
            coverageBehavior = SkslCoverageBehavior.PremultipliedCoverageHomogeneous;
        }

        stage = new StageCandidate(
            fragment,
            new SkslSnippetStage(description, coverageBehavior));
        return true;
    }

    private static bool TryClassifyCompatibility(
        RenderFragmentReference reference,
        IReadOnlyDictionary<RenderFragmentReference, CompatibilityClassification> rejectedStageClassifications,
        out CompatibilityClassification result)
    {
        if (rejectedStageClassifications.TryGetValue(reference, out result))
            return true;

        result = reference.Kind switch
        {
            RenderFragmentKind.Opacity => new(ExecutionIslandKind.Compatibility,
                ExecutionIslandBoundaryReason.SemanticComposite, []),
            RenderFragmentKind.Shader => new(ExecutionIslandKind.Compatibility,
                ExecutionIslandBoundaryReason.WholeSourceShader, []),
            RenderFragmentKind.Geometry => new(ExecutionIslandKind.Compatibility,
                ExecutionIslandBoundaryReason.Geometry, []),
            RenderFragmentKind.OpaqueSource
                or RenderFragmentKind.OpaqueMap
                or RenderFragmentKind.OpaqueCombine
                or RenderFragmentKind.OpaqueExpand
                when reference.Payload is OpaqueRenderFragmentPayload opaque
                     && opaque.Description.BackendBoundary
                     == RenderBackendBoundary.Graphics3D => new(ExecutionIslandKind.Compatibility,
                         ExecutionIslandBoundaryReason.ThreeD, []),
            RenderFragmentKind.OpaqueSource
                or RenderFragmentKind.OpaqueMap
                or RenderFragmentKind.OpaqueCombine
                or RenderFragmentKind.OpaqueExpand => new(ExecutionIslandKind.Compatibility,
                    ExecutionIslandBoundaryReason.Opaque, []),
            RenderFragmentKind.LegacyFilterEffect => new(ExecutionIslandKind.Compatibility,
                ExecutionIslandBoundaryReason.LegacyCustomEffect, []),
            RenderFragmentKind.TargetCapture
                or RenderFragmentKind.BuiltInBackdropCapture => new(ExecutionIslandKind.Target,
                    ExecutionIslandBoundaryReason.TargetCapture, []),
            RenderFragmentKind.Layer => new(ExecutionIslandKind.Target,
                ExecutionIslandBoundaryReason.Layer, []),
            RenderFragmentKind.TargetLayerScope
                or RenderFragmentKind.TargetScope => new(ExecutionIslandKind.Target,
                    ExecutionIslandBoundaryReason.TargetScope, []),
            RenderFragmentKind.RawTargetScope
                or RenderFragmentKind.RawTargetCommand => new(ExecutionIslandKind.Target,
                    ExecutionIslandBoundaryReason.LegacyRawCanvas, []),
            RenderFragmentKind.TargetCommand
                when ((TargetCommandRenderFragmentPayload)reference.Payload!).Description.Access
                     == TargetAccess.Readback => new(ExecutionIslandKind.Readback,
                         ExecutionIslandBoundaryReason.Readback, []),
            RenderFragmentKind.TargetCommand => new(ExecutionIslandKind.Target,
                ExecutionIslandBoundaryReason.TargetCommand, []),
            RenderFragmentKind.Blend
                or RenderFragmentKind.OpacityMask => new(ExecutionIslandKind.Compatibility,
                    ExecutionIslandBoundaryReason.UnsafeComposite, []),
            _ => default,
        };
        return result != default;
    }

    private static bool RequiresDeclaredReadback(RenderFragmentReference reference)
        => reference.Payload switch
        {
            GeometryRenderFragmentPayload geometry => geometry.Description.RequiresReadback,
            OpaqueRenderFragmentPayload opaque => opaque.Description.RequiresReadback,
            TargetCommandRenderFragmentPayload command
                => command.Description.Access == TargetAccess.Readback
                   || command.Description.RequiresInputReadback,
            _ => false,
        };

    private static bool PlansGpuPass(RenderFragmentReference reference)
        => reference.Kind switch
        {
            RenderFragmentKind.Opacity
                or RenderFragmentKind.Blend
                or RenderFragmentKind.OpacityMask
                or RenderFragmentKind.Shader
                or RenderFragmentKind.Geometry
                or RenderFragmentKind.OpaqueSource
                or RenderFragmentKind.OpaqueMap
                or RenderFragmentKind.OpaqueCombine
                or RenderFragmentKind.OpaqueExpand
                or RenderFragmentKind.Layer
                or RenderFragmentKind.TargetCapture
                or RenderFragmentKind.BuiltInBackdropCapture => true,
            RenderFragmentKind.TargetLayerScope
                => ((TargetLayerScopeRenderFragmentPayload)reference.Payload!).Region.Kind
                   != TargetRegionKind.Empty,
            RenderFragmentKind.TargetCommand
                => ((TargetCommandRenderFragmentPayload)reference.Payload!).Description.AffectedRegion.Kind
                   != TargetRegionKind.Empty,
            RenderFragmentKind.TargetScope
                => ((TargetScopeRenderFragmentPayload)reference.Payload!).Description.IsValueReplayMap,
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

    private static ImmutableArray<CompiledShaderStage> CreateCompiledStages(ProgramGroup group)
    {
        if (group.Stages.Count != group.Program.Stages.Count)
            throw new InvalidOperationException("A merged program lost its semantic stage mapping.");

        var result = ImmutableArray.CreateBuilder<CompiledShaderStage>(group.Stages.Count);
        for (int index = 0; index < group.Stages.Count; index++)
        {
            result.Add(group.Stages[index].ToCompiledStage(
                group.Program.Stages[index].StageIndex));
        }
        return result.MoveToImmutable();
    }

    private static RenderFragmentId GetId(RenderFragmentReference reference)
        => reference.Id
           ?? throw new InvalidOperationException("An execution-planner fragment is not committed.");

    private sealed record StageCandidate(
        RenderFragmentReference Fragment,
        SkslSnippetStage Snippet)
    {
        public CompiledShaderStage ToCompiledStage(int programStageIndex)
            => new(
                GetId(Fragment),
                Fragment,
                Fragment.Kind,
                Snippet.Description,
                Snippet.CoverageBehavior,
                programStageIndex);
    }

    private sealed record ProgramGroup(
        IReadOnlyList<StageCandidate> Stages,
        SkslMergedProgram Program);

    private sealed record IslandDraft(
        long AuthoredOrder,
        ExecutionIslandKind Kind,
        ImmutableArray<RenderFragmentId> Fragments,
        bool PlansGpuPass,
        RenderFragmentReference? Input,
        RenderFragmentReference? Output,
        ImmutableArray<CompiledShaderStage> Stages,
        SkslMergedProgram? Program,
        ShaderRunCoverageSource CoverageSource);

    private readonly record struct CompatibilityClassification(
        ExecutionIslandKind Kind,
        ExecutionIslandBoundaryReason Reason,
        ImmutableArray<SkslBackendLimit> BackendLimits);

    private sealed class ExecutionIslandBoundaryComparer : IEqualityComparer<ExecutionIslandBoundary>
    {
        public static ExecutionIslandBoundaryComparer Instance { get; } = new();

        public bool Equals(ExecutionIslandBoundary x, ExecutionIslandBoundary y)
            => x.BeforeFragmentId == y.BeforeFragmentId
               && x.AfterFragmentId == y.AfterFragmentId
               && x.Reason == y.Reason
               && x.BackendLimits.AsSpan().SequenceEqual(y.BackendLimits.AsSpan());

        public int GetHashCode(ExecutionIslandBoundary obj)
        {
            var hash = new HashCode();
            hash.Add(obj.BeforeFragmentId);
            hash.Add(obj.AfterFragmentId);
            hash.Add(obj.Reason);
            foreach (SkslBackendLimit limit in obj.BackendLimits)
                hash.Add(limit);
            return hash.ToHashCode();
        }
    }
}
