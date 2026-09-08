using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Beutl.Media;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed record RenderMaterializationDemandResolution(
    IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> Demands,
    IReadOnlySet<RenderFragmentReference> PreviewDropEligibleMaterializations);

/// <summary>
/// Resolves cache candidates only after target dependencies, metadata, and required regions are known. It does
/// not mutate the recorded graph: substitutions and capture points refer back to the original producer, leaving
/// every fragment input and target-token edge intact.
/// </summary>
internal sealed class RenderCacheResolver
{
    private const int MaximumResolutionPasses = 4;

    public RenderCachePlanningResult Resolve(
        RenderRequest request,
        RecordedRenderGraph graph,
        RegionAnalysis regions,
        IReadOnlyList<RenderFragmentReference> roots,
        RenderCacheResolutionContext context,
        IRenderCacheLookup? lookup = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(roots);
        context.Format.ThrowIfUninitialized(nameof(context));
        context.DeviceContext.ThrowIfUninitialized(nameof(context));
        ValidateRequest(request, graph);

        var index = new ResolverIndex(graph);
        var lookupMemo = new LookupMemo(lookup);
        var planningBoundaries = new HashSet<RenderFragmentReference>(
            ReferenceEqualityComparer.Instance);
        var visitedBoundarySets = new HashSet<HashSet<RenderFragmentReference>>(
            RenderFragmentReferenceSetComparer.Instance)
        {
            planningBoundaries,
        };
        RenderMaterializationDemandResolution? uncachedDemandResolution = null;
        // Selecting a hit or miss changes a fragment from target replay to value
        // materialization, which can change descendant density and therefore identity.
        // Resolve every candidate independently while finding the fixed point. Parent-hit
        // supersedence is an execution selection and must not remove a child from density
        // planning before the ancestor identity that selected the hit is stable.
        for (int pass = 1; pass <= MaximumResolutionPasses; pass++)
        {
            RenderMaterializationDemandResolution demandResolution =
                RenderMaterializationDemandResolver.Resolve(
                    roots,
                    request.Options.OutputScale,
                    request.Options.MaxWorkingScale,
                    planningBoundaries);
            IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> demands = demandResolution.Demands;
            uncachedDemandResolution ??= demandResolution;
            HashSet<RenderFragmentReference> nextPlanningBoundaries =
                ResolvePlanningBoundaries(
                    request,
                    index,
                    regions,
                    demands,
                    context,
                    lookupMemo);
            if (nextPlanningBoundaries.SetEquals(planningBoundaries))
            {
                RenderCacheResolution resolution = ResolveFinal(
                    request,
                    index,
                    regions,
                    demands,
                    context,
                    lookupMemo);
                return new RenderCachePlanningResult(
                    resolution,
                    demands,
                    demandResolution.PreviewDropEligibleMaterializations);
            }

            if (!visitedBoundarySets.Add(nextPlanningBoundaries))
            {
                return CreateUnstableBoundaryFallback(graph, uncachedDemandResolution!);
            }

            planningBoundaries = nextPlanningBoundaries;
        }

        return CreateUnstableBoundaryFallback(graph, uncachedDemandResolution!);
    }

    private static HashSet<RenderFragmentReference> ResolvePlanningBoundaries(
        RenderRequest request,
        ResolverIndex index,
        RegionAnalysis regions,
        IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> materializationDemands,
        RenderCacheResolutionContext context,
        LookupMemo lookupMemo)
    {
        var result = new HashSet<RenderFragmentReference>(
            ReferenceEqualityComparer.Instance);
        Dictionary<RenderFragmentOutputIdentityMemoKey, RenderFragmentOutputIdentity>? identityMemo =
            context.AllowCapturePublication
                ? null
                : [];
        foreach (RenderCacheCandidate candidate in index.Graph.CacheCandidates)
        {
            RenderFragmentReference reference = index.Graph.GetFragment(candidate.FragmentId);
            CandidateEvaluation evaluation = EvaluateCandidate(
                request,
                reference,
                regions,
                context,
                materializationDemands,
                index.DeviceGridAffectedReferences,
                index.TransformDependentReferences);
            if (evaluation.BypassReason != RenderCacheBypassReason.None)
                continue;

            if (context.AllowCapturePublication)
            {
                result.Add(reference);
                continue;
            }

            RenderOutputCacheIdentity identity = CreateIdentity(
                request,
                candidate,
                reference,
                evaluation,
                regions,
                context,
                materializationDemands,
                identityMemo!);
            if (context.AllowPersistentLookup
                && lookupMemo.TryGet(candidate, identity, out _))
            {
                result.Add(reference);
            }
        }

        return result;
    }

    private static RenderCacheResolution ResolveFinal(
        RenderRequest request,
        ResolverIndex index,
        RegionAnalysis regions,
        IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> materializationDemands,
        RenderCacheResolutionContext context,
        LookupMemo lookupMemo)
    {
        CandidateTopology? topology =
            context.AllowPersistentLookup
            && lookupMemo.HasLookup
            && index.Graph.CacheCandidates.Length > 1
                ? index.GetTopology()
                : null;
        IReadOnlyList<RenderCacheCandidate> candidates = topology is null
            ? index.Graph.CacheCandidates
            : topology.ParentFirst;
        var identityMemo = new Dictionary<RenderFragmentOutputIdentityMemoKey, RenderFragmentOutputIdentity>();
        var decisions = new RenderCacheDecision[index.Graph.CacheCandidates.Length];
        var selectedHits = new List<RenderCacheCandidateId>();
        foreach (RenderCacheCandidate candidate in candidates)
        {
            if (topology is not null)
            {
                RenderCacheCandidateId superseding = default;
                for (int hit = 0; hit < selectedHits.Count; hit++)
                {
                    if (topology.Descendants[selectedHits[hit]].Contains(candidate.Id))
                    {
                        superseding = selectedHits[hit];
                        break;
                    }
                }

                if (superseding.Value > 0)
                {
                    decisions[GetCandidateIndex(candidate.Id)] = Superseded(candidate, superseding);
                    continue;
                }
            }

            RenderCacheDecision decision = ResolveCandidate(
                request,
                candidate,
                index.Graph.GetFragment(candidate.FragmentId),
                regions,
                context,
                materializationDemands,
                lookupMemo,
                identityMemo,
                index.DeviceGridAffectedReferences,
                index.TransformDependentReferences);
            decisions[GetCandidateIndex(candidate.Id)] = decision;
            if (decision.Kind == RenderCacheResolutionKind.Hit)
                selectedHits.Add(candidate.Id);
        }

        return new RenderCacheResolution(ImmutableCollectionsMarshal.AsImmutableArray(decisions));
    }

    private static void ValidateRequest(
        RenderRequest request,
        RecordedRenderGraph graph)
    {
        if (request.Id != graph.RequestId)
        {
            throw new ArgumentException(
                "The recorded graph belongs to a different render request.",
                nameof(graph));
        }
        if (request.State != RenderRequestState.RegionsResolved)
        {
            throw new InvalidOperationException(
                "Render-cache resolution requires completed graph, target-dependency, metadata, and region discovery.");
        }
    }

    private static RenderCachePlanningResult CreateUnstableBoundaryFallback(
        RecordedRenderGraph graph,
        RenderMaterializationDemandResolution uncachedDemandResolution)
    {
        var resolution = new RenderCacheResolution(
            [.. graph.CacheCandidates.Select(candidate =>
                Bypass(candidate, RenderCacheBypassReason.UnstableBoundaryPlan))]);
        return new RenderCachePlanningResult(
            resolution,
            uncachedDemandResolution.Demands,
            uncachedDemandResolution.PreviewDropEligibleMaterializations);
    }

    private static RenderCacheDecision Superseded(
        RenderCacheCandidate candidate,
        RenderCacheCandidateId superseding)
        => new(
            candidate,
            RenderCacheResolutionKind.Superseded,
            RenderCacheBypassReason.None,
            null,
            null,
            superseding);

    private static int GetCandidateIndex(RenderCacheCandidateId id)
        => checked((int)id.Value - 1);

    private static RenderCacheDecision ResolveCandidate(
        RenderRequest request,
        RenderCacheCandidate candidate,
        RenderFragmentReference reference,
        RegionAnalysis regions,
        RenderCacheResolutionContext context,
        IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> materializationDemands,
        LookupMemo lookupMemo,
        IDictionary<RenderFragmentOutputIdentityMemoKey, RenderFragmentOutputIdentity> identityMemo,
        IReadOnlySet<RenderFragmentReference> deviceGridAffectedReferences,
        IReadOnlySet<RenderFragmentReference> transformDependentReferences)
    {
        CandidateEvaluation evaluation = EvaluateCandidate(
            request,
            reference,
            regions,
            context,
            materializationDemands,
            deviceGridAffectedReferences,
            transformDependentReferences);
        if (evaluation.BypassReason != RenderCacheBypassReason.None)
            return Bypass(candidate, evaluation.BypassReason);

        RenderOutputCacheIdentity identity = CreateIdentity(
            request,
            candidate,
            reference,
            evaluation,
            regions,
            context,
            materializationDemands,
            identityMemo);

        if (context.AllowPersistentLookup
            && lookupMemo.TryGet(candidate, identity, out RenderCacheEntry? entry))
        {
            return new RenderCacheDecision(
                candidate,
                RenderCacheResolutionKind.Hit,
                RenderCacheBypassReason.None,
                null,
                entry);
        }

        if (!context.AllowCapturePublication)
            return Bypass(candidate, RenderCacheBypassReason.CapturePublicationDisabled);

        return new RenderCacheDecision(
            candidate,
            RenderCacheResolutionKind.MissCapture,
            RenderCacheBypassReason.None,
            identity,
            null);
    }

    private static RenderOutputCacheIdentity CreateIdentity(
        RenderRequest request,
        RenderCacheCandidate candidate,
        RenderFragmentReference reference,
        CandidateEvaluation evaluation,
        RegionAnalysis regions,
        RenderCacheResolutionContext context,
        IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> materializationDemands,
        IDictionary<RenderFragmentOutputIdentityMemoKey, RenderFragmentOutputIdentity> identityMemo)
        => new(
            candidate.CacheKey,
            RenderFragmentOutputIdentity.Create(
                reference,
                graphRequestId: request.Id,
                materializationDemands,
                identityMemo,
                request.Options.OutputScale,
                request.Options.MaxWorkingScale,
                regions),
            evaluation.Metadata.Bounds,
            evaluation.Coverage,
            evaluation.Density,
            context.Format,
            request.Options.Intent,
            request.Options.Purpose,
            request.Options.FusionMode,
            context.DeviceContext,
            evaluation.DeviceGridOffset);

    private static CandidateEvaluation EvaluateCandidate(
        RenderRequest request,
        RenderFragmentReference reference,
        RegionAnalysis regions,
        RenderCacheResolutionContext context,
        IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> materializationDemands,
        IReadOnlySet<RenderFragmentReference> deviceGridAffectedReferences,
        IReadOnlySet<RenderFragmentReference> transformDependentReferences)
    {
        if (!request.Options.CachePolicy.IsEnabled)
            return CandidateEvaluation.Bypass(RenderCacheBypassReason.CacheDisabled);
        if (request.Options.Purpose is RenderRequestPurpose.Bounds or RenderRequestPurpose.HitTest)
            return CandidateEvaluation.Bypass(RenderCacheBypassReason.MetadataOnlyPurpose);
        if (!context.AllowPersistentLookup && !context.AllowCapturePublication)
            return CandidateEvaluation.Bypass(RenderCacheBypassReason.PersistentLookupDisabled);
        if (ContainsRawTargetWork(reference))
            return CandidateEvaluation.Bypass(RenderCacheBypassReason.RawTargetWork);
        if (RenderFragmentTargetDependency.HasExternalTargetDependency(reference))
            return CandidateEvaluation.Bypass(RenderCacheBypassReason.TargetTokenDependency);
        if (!reference.CanBeUsedAsValueInput || reference.ValueCardinality.Maximum == 0)
            return CandidateEvaluation.Bypass(RenderCacheBypassReason.NotMaterializable);
        if (reference.Kind == RenderFragmentKind.MaterializedInput
            && reference.Payload is MaterializedInputRenderFragmentPayload input
            && (input.Description.DeviceBounds.Width > RenderScaleUtilities.MaxBufferDimension
                || input.Description.DeviceBounds.Height > RenderScaleUtilities.MaxBufferDimension))
        {
            return CandidateEvaluation.Bypass(
                RenderCacheBypassReason.ExternalInputExceedsBufferBudget);
        }

        RenderFragmentId fragmentId = reference.Id
            ?? throw new InvalidOperationException("A cache candidate refers to an uncommitted fragment.");
        if (!regions.FragmentRequirements.TryGetValue(fragmentId, out RequiredRegion requirement)
            || !regions.Metadata.TryGetValue(fragmentId, out ResolvedFragmentMetadata metadata))
        {
            return CandidateEvaluation.Bypass(RenderCacheBypassReason.EmptyRequirement);
        }
        if (requirement.IsEmpty)
            return CandidateEvaluation.Bypass(RenderCacheBypassReason.EmptyRequirement);

        float density = ResolveMaterializationDensity(
            reference,
            materializationDemands);
        if (transformDependentReferences.Contains(reference)
            || (deviceGridAffectedReferences.Contains(reference)
                && DeviceGridAlignment.NormalizePhase(context.DeviceGridOffset, density) != default))
        {
            return CandidateEvaluation.Bypass(RenderCacheBypassReason.DeviceGridDependentOutput);
        }

        if (!TryResolveCacheCaptureSize(
                reference,
                regions,
                metadata,
                requirement,
                density,
                out PixelSize captureSize))
        {
            return CandidateEvaluation.Bypass(RenderCacheBypassReason.NotMaterializable);
        }
        if (!request.Options.CachePolicy.Rules.Match(captureSize))
        {
            return CandidateEvaluation.Bypass(RenderCacheBypassReason.OutsideCacheRules);
        }

        return CandidateEvaluation.Eligible(
            metadata,
            requirement,
            density,
            deviceGridAffectedReferences.Contains(reference)
                ? context.DeviceGridOffset
                : default);
    }

    private static bool TryResolveCacheCaptureSize(
        RenderFragmentReference reference,
        RegionAnalysis regions,
        ResolvedFragmentMetadata metadata,
        RequiredRegion requirement,
        float density,
        out PixelSize result)
    {
        if (reference.Kind == RenderFragmentKind.ContributeValues
            && !reference.Inputs.IsDefaultOrEmpty)
        {
            if (reference.Inputs.Length != 1
                || reference.Inputs[0].Id is not { } inputId
                || !regions.Metadata.TryGetValue(inputId, out ResolvedFragmentMetadata inputMetadata)
                || !regions.FragmentRequirements.TryGetValue(inputId, out RequiredRegion inputRequirement))
            {
                result = default;
                return false;
            }

            RenderFragmentReference input = reference.Inputs[0];
            return TryResolveCacheCaptureSize(
                input,
                regions,
                inputMetadata,
                inputRequirement,
                RenderMaterializationDensityPolicy.Clamp(input, density),
                out result);
        }

        if (reference.Kind == RenderFragmentKind.MaterializedInput
            && reference.Payload is MaterializedInputRenderFragmentPayload materializedInput)
        {
            result = materializedInput.Description.DeviceBounds.Size;
            return true;
        }

        Rect captureBounds = reference.Kind == RenderFragmentKind.Layer
                             && reference.Payload is LayerRenderFragmentPayload layer
            ? layer.Domain ?? reference.Bounds
            : reference.Kind is RenderFragmentKind.Opacity
                or RenderFragmentKind.OpacityMask
                or RenderFragmentKind.OpaqueSource
                or RenderFragmentKind.OpaqueMap
                or RenderFragmentKind.OpaqueCombine
                or RenderFragmentKind.OpaqueExpand
                or RenderFragmentKind.TargetCapture
                or RenderFragmentKind.BuiltInBackdropCapture
                ? reference.Bounds
                : requirement.IsFull
                    ? metadata.Bounds
                    : requirement.Value;
        PixelRect deviceBounds = PixelRect.FromRect(captureBounds, density);
        bool requiresRasterApron =
            reference.Kind == RenderFragmentKind.TargetScope
            && reference.Payload is TargetScopeRenderFragmentPayload targetScope
            && targetScope.Description.IsValueReplayMap
            || reference.Kind == RenderFragmentKind.OpaqueSource
            && reference.Payload is OpaqueRenderFragmentPayload opaque
            && opaque.Description.HasDirectReplayMaterializationContract;
        if (requiresRasterApron)
        {
            deviceBounds = RenderScaleUtilities.AddRasterApron(deviceBounds);
        }

        result = deviceBounds.Size;
        return true;
    }

    private static float ResolveMaterializationDensity(
        RenderFragmentReference reference,
        IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> materializationDemands)
    {
        if (!materializationDemands.TryGetValue(reference, out EffectiveScale demand))
        {
            throw new InvalidOperationException(
                "A cache candidate is not reachable from the request publication roots.");
        }

        float density = demand.Value;
        return RenderMaterializationDensityPolicy.Clamp(reference, density);
    }

    private static bool ContainsRawTargetWork(RenderFragmentReference reference)
    {
        var visited = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<RenderFragmentReference>();
        pending.Push(reference);
        while (pending.TryPop(out RenderFragmentReference? current))
        {
            if (!visited.Add(current))
                continue;
            if (current.Kind is RenderFragmentKind.RawTargetScope
                or RenderFragmentKind.RawTargetCommand
                || current.Kind == RenderFragmentKind.FilterEffectSegment
                && !FilterEffectSegmentDirectReplaySupport.CanMaterialize(current))
            {
                return true;
            }

            foreach (RenderFragmentReference input in current.Inputs)
                pending.Push(input);
        }
        return false;
    }

    private static RenderCacheDecision Bypass(
        RenderCacheCandidate candidate,
        RenderCacheBypassReason reason)
        => new(
            candidate,
            RenderCacheResolutionKind.Bypass,
            reason,
            null,
            null);

    private readonly record struct CandidateEvaluation(
        RenderCacheBypassReason BypassReason,
        ResolvedFragmentMetadata Metadata,
        RequiredRegion Coverage,
        float Density,
        Vector DeviceGridOffset)
    {
        public static CandidateEvaluation Bypass(RenderCacheBypassReason reason)
            => new(reason, default, default, default, default);

        public static CandidateEvaluation Eligible(
            ResolvedFragmentMetadata metadata,
            RequiredRegion coverage,
            float density,
            Vector deviceGridOffset = default)
            => new(RenderCacheBypassReason.None, metadata, coverage, density, deviceGridOffset);
    }

    private sealed class ResolverIndex
    {
        private CandidateTopology? _topology;

        public ResolverIndex(RecordedRenderGraph graph)
        {
            Graph = graph;
            var deviceGridReferences = ResolveDeviceGridReferences(graph.Fragments);
            DeviceGridAffectedReferences = deviceGridReferences.Affected;
            TransformDependentReferences = deviceGridReferences.TransformDependent;
        }

        public RecordedRenderGraph Graph { get; }

        public HashSet<RenderFragmentReference> DeviceGridAffectedReferences { get; }

        public HashSet<RenderFragmentReference> TransformDependentReferences { get; }

        public CandidateTopology GetTopology()
            => _topology ??= BuildCandidateTopology(Graph);

        private static (
            HashSet<RenderFragmentReference> Affected,
            HashSet<RenderFragmentReference> TransformDependent) ResolveDeviceGridReferences(
                ImmutableArray<RenderFragmentReference> references)
        {
            var consumers = new Dictionary<RenderFragmentReference, List<RenderFragmentReference>>(
                ReferenceEqualityComparer.Instance);
            foreach (RenderFragmentReference reference in references)
                consumers.Add(reference, []);
            foreach (RenderFragmentReference reference in references)
            {
                foreach (RenderFragmentReference input in reference.Inputs)
                    consumers[input].Add(reference);
            }

            RenderFragmentReference[] phaseUnsafeMaskScopes =
            [
                .. references.Where(IsPhaseUnsafeMaskScope),
            ];
            RenderFragmentReference[] sensitive =
            [
                .. references.Where(IsDeviceGridSensitive),
                .. phaseUnsafeMaskScopes,
            ];
            HashSet<RenderFragmentReference> affected = ExpandConnectedReferences(
                sensitive,
                consumers);
            var transformRootList = new List<RenderFragmentReference>();
            for (int index = 0; index < sensitive.Length; index++)
            {
                if (HasGridRemappingAncestor(sensitive[index], consumers))
                    transformRootList.Add(sensitive[index]);
            }

            RenderFragmentReference[] transformRoots = [.. transformRootList];
            HashSet<RenderFragmentReference> transformDependent = ExpandConnectedReferences(
                transformRoots,
                consumers);
            transformDependent.UnionWith(ExpandConnectedReferences(
                phaseUnsafeMaskScopes,
                consumers));
            return (affected, transformDependent);
        }

        private static HashSet<RenderFragmentReference> ExpandConnectedReferences(
            IEnumerable<RenderFragmentReference> roots,
            IReadOnlyDictionary<RenderFragmentReference, List<RenderFragmentReference>> consumers)
        {
            var result = new HashSet<RenderFragmentReference>(
                ReferenceEqualityComparer.Instance);
            var pending = new Stack<RenderFragmentReference>(roots);
            while (pending.TryPop(out RenderFragmentReference? current))
            {
                if (!result.Add(current))
                    continue;
                foreach (RenderFragmentReference input in current.Inputs)
                    pending.Push(input);
            }

            var visitedAncestors = new HashSet<RenderFragmentReference>(
                ReferenceEqualityComparer.Instance);
            pending = new Stack<RenderFragmentReference>(roots);
            while (pending.TryPop(out RenderFragmentReference? current))
            {
                if (!visitedAncestors.Add(current))
                    continue;
                result.Add(current);
                foreach (RenderFragmentReference consumer in consumers[current])
                    pending.Push(consumer);
            }

            return result;
        }

        private static bool IsDeviceGridSensitive(RenderFragmentReference reference)
        {
            if (reference.Kind is RenderFragmentKind.FilterEffectSegment
                or RenderFragmentKind.Shader
                or RenderFragmentKind.Geometry)
            {
                return true;
            }

            if (reference.Payload is OpaqueRenderFragmentPayload opaque)
            {
                bool declaresPhaseDependence = opaque.Description.DeviceGridSensitivity
                                               == RenderDeviceGridSensitivity.PhaseDependent;
                bool isDrawableBrushHost = reference.Kind == RenderFragmentKind.OpaqueCombine
                                           && opaque.Description.HasDirectReplayMaterializationContract;
                if (declaresPhaseDependence || isDrawableBrushHost)
                    return true;
            }

            if (reference.Payload is TargetScopeRenderFragmentPayload scope
                && scope.Description.DeviceGridSensitivity == RenderDeviceGridSensitivity.PhaseDependent)
            {
                return true;
            }

            return false;
        }

        private static bool IsPhaseUnsafeMaskScope(RenderFragmentReference reference)
        {
            if (reference.Kind != RenderFragmentKind.TargetLayerScope)
                return false;

            var visited = new HashSet<RenderFragmentReference>(
                ReferenceEqualityComparer.Instance);
            var pending = new Stack<RenderFragmentReference>();
            pending.Push(reference);
            while (pending.TryPop(out RenderFragmentReference? current))
            {
                if (!visited.Add(current))
                    continue;
                if (current.Kind == RenderFragmentKind.Blend
                    && ((BlendRenderFragmentPayload)current.Payload!).BlendMode
                    is BlendMode.DstIn or BlendMode.SrcIn or BlendMode.DstATop)
                {
                    return true;
                }

                foreach (RenderFragmentReference input in current.Inputs)
                    pending.Push(input);
            }

            return false;
        }

        private static bool HasGridRemappingAncestor(
            RenderFragmentReference reference,
            IReadOnlyDictionary<RenderFragmentReference, List<RenderFragmentReference>> consumers)
        {
            var visited = new HashSet<RenderFragmentReference>(
                ReferenceEqualityComparer.Instance);
            var pending = new Stack<RenderFragmentReference>(consumers[reference]);
            while (pending.TryPop(out RenderFragmentReference? current))
            {
                if (!visited.Add(current))
                    continue;
                if (RenderFragmentDeviceGrid.ResolveMapping(current)
                    == RenderDeviceGridMapping.Remapped)
                {
                    return true;
                }

                foreach (RenderFragmentReference consumer in consumers[current])
                    pending.Push(consumer);
            }

            return false;
        }
    }

    internal sealed record CandidateTopology(
        Dictionary<RenderCacheCandidateId, HashSet<RenderCacheCandidateId>> Descendants,
        RenderCacheCandidate[] ParentFirst);

    private sealed class LookupMemo(IRenderCacheLookup? lookup)
    {
        private readonly Dictionary<RenderCacheCandidateId,
            Dictionary<RenderOutputCacheIdentity, RenderCacheEntry?>> _entries = [];

        public bool HasLookup => lookup is not null;

        public bool TryGet(
            RenderCacheCandidate candidate,
            RenderOutputCacheIdentity identity,
            out RenderCacheEntry? entry)
        {
            if (lookup is null)
            {
                entry = null;
                return false;
            }

            if (!_entries.TryGetValue(candidate.Id, out var candidates))
            {
                candidates = [];
                _entries.Add(candidate.Id, candidates);
            }
            else if (candidates.TryGetValue(identity, out entry))
            {
                return entry is not null;
            }

            bool found = lookup.TryGet(candidate, identity, out RenderCacheEntry? candidateEntry)
                         && candidateEntry is not null
                         && candidateEntry.Identity.Equals(identity);
            entry = found ? candidateEntry : null;
            candidates.Add(identity, entry);
            return found;
        }
    }

    private sealed class RenderFragmentReferenceSetComparer
        : IEqualityComparer<HashSet<RenderFragmentReference>>
    {
        public static RenderFragmentReferenceSetComparer Instance { get; } = new();

        public bool Equals(
            HashSet<RenderFragmentReference>? x,
            HashSet<RenderFragmentReference>? y)
            => ReferenceEquals(x, y)
               || x is not null
               && y is not null
               && x.SetEquals(y);

        public int GetHashCode(HashSet<RenderFragmentReference> set)
        {
            int hash = set.Count;
            foreach (RenderFragmentReference reference in set)
                hash ^= ReferenceEqualityComparer.Instance.GetHashCode(reference);
            return hash;
        }
    }

    internal static CandidateTopology BuildCandidateTopology(
        RecordedRenderGraph graph)
    {
        var result = new Dictionary<RenderCacheCandidateId, HashSet<RenderCacheCandidateId>>();
        var reachable = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<RenderFragmentReference>();
        foreach (RenderCacheCandidate parent in graph.CacheCandidates)
        {
            var descendants = new HashSet<RenderCacheCandidateId>();
            bool reachableResolved = false;
            foreach (RenderCacheCandidate child in graph.CacheCandidates)
            {
                if (parent.Id == child.Id)
                    continue;
                if (parent.FragmentId == child.FragmentId)
                {
                    if (parent.AuthoredOrder > child.AuthoredOrder)
                        descendants.Add(child.Id);
                    continue;
                }

                if (!reachableResolved)
                {
                    CollectReachableInputs(graph.GetFragment(parent.FragmentId), reachable, pending);
                    reachableResolved = true;
                }

                if (reachable.Contains(graph.GetFragment(child.FragmentId)))
                    descendants.Add(child.Id);
            }
            result.Add(parent.Id, descendants);
        }
        RenderCacheCandidate[] parentFirst = [.. graph.CacheCandidates
            .OrderByDescending(candidate => result[candidate.Id].Count)
            .ThenByDescending(static candidate => candidate.AuthoredOrder)];
        return new CandidateTopology(result, parentFirst);
    }

    private static void CollectReachableInputs(
        RenderFragmentReference parent,
        HashSet<RenderFragmentReference> reachable,
        Stack<RenderFragmentReference> pending)
    {
        reachable.Clear();
        pending.Clear();
        foreach (RenderFragmentReference input in parent.Inputs)
            pending.Push(input);
        while (pending.TryPop(out RenderFragmentReference? current))
        {
            if (!reachable.Add(current))
                continue;
            foreach (RenderFragmentReference input in current.Inputs)
                pending.Push(input);
        }
    }
}

internal readonly record struct RenderFragmentOutputIdentityMemoKey(RenderFragmentReference Reference);
