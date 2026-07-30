using System.Collections.Immutable;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

internal readonly record struct RenderCacheFormatIdentity(
    string PixelFormat,
    string AlphaType,
    string ColorSpace)
{
    public static RenderCacheFormatIdentity LinearPremultipliedRgba16Float { get; } =
        new("RGBA16Float", "Premultiplied", "LinearSrgb");

    public void ThrowIfUninitialized(string parameterName)
    {
        if (string.IsNullOrWhiteSpace(PixelFormat)
            || string.IsNullOrWhiteSpace(AlphaType)
            || string.IsNullOrWhiteSpace(ColorSpace))
        {
            throw new ArgumentException(
                "A render-cache format identity must name its pixel, alpha, and color-space contracts.",
                parameterName);
        }
    }
}

internal readonly record struct RenderCacheDeviceContextIdentity(
    object DeviceIdentity,
    object ContextIdentity)
{
    public void ThrowIfUninitialized(string parameterName)
    {
        if (DeviceIdentity is null || ContextIdentity is null)
        {
            throw new ArgumentException(
                "A render-cache device identity requires non-null device and context components.",
                parameterName);
        }
    }
}

internal readonly record struct RenderCacheResolutionContext
{
    public RenderCacheResolutionContext(
        RenderCacheFormatIdentity format,
        RenderCacheDeviceContextIdentity deviceContext,
        bool allowPersistentLookup = true,
        bool allowCapturePublication = true,
        Vector deviceGridOffset = default)
    {
        format.ThrowIfUninitialized(nameof(format));
        deviceContext.ThrowIfUninitialized(nameof(deviceContext));
        Format = format;
        DeviceContext = deviceContext;
        AllowPersistentLookup = allowPersistentLookup;
        AllowCapturePublication = allowCapturePublication;
        DeviceGridOffset = deviceGridOffset;
    }

    public RenderCacheFormatIdentity Format { get; }

    public RenderCacheDeviceContextIdentity DeviceContext { get; }

    public bool AllowPersistentLookup { get; }

    public bool AllowCapturePublication { get; }

    public Vector DeviceGridOffset { get; }
}

/// <summary>
/// Complete runtime identity for one materialized render-cache value. The hash is a bucket hint only;
/// <see cref="Equals(RenderOutputCacheIdentity?)"/> compares every retained component.
/// </summary>
internal sealed class RenderOutputCacheIdentity : IEquatable<RenderOutputCacheIdentity>
{
    private readonly object _candidateKey;
    private readonly RenderFragmentOutputIdentity _fragment;
    private readonly Rect _bounds;
    private readonly RequiredRegion _coverage;
    private readonly int _densityBits;
    private readonly RenderCacheFormatIdentity _format;
    private readonly RenderIntent _intent;
    private readonly RenderRequestPurpose _purpose;
    private readonly RenderCacheDeviceContextIdentity _deviceContext;
    private readonly Vector _deviceGridOffset;

    public RenderOutputCacheIdentity(
        object candidateKey,
        RenderFragmentOutputIdentity fragment,
        Rect bounds,
        RequiredRegion coverage,
        float density,
        RenderCacheFormatIdentity format,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        RenderCacheDeviceContextIdentity deviceContext,
        Vector deviceGridOffset = default)
    {
        ArgumentNullException.ThrowIfNull(candidateKey);
        ArgumentNullException.ThrowIfNull(fragment);
        if (!RenderRectValidation.IsFiniteNonNegative(bounds))
            throw new ArgumentException("Cache bounds must be finite and non-negative.", nameof(bounds));
        if (!float.IsFinite(density) || density <= 0)
            throw new ArgumentOutOfRangeException(nameof(density), density, "Cache density must be finite and positive.");
        format.ThrowIfUninitialized(nameof(format));
        deviceContext.ThrowIfUninitialized(nameof(deviceContext));
        if (!Enum.IsDefined(intent))
            throw new ArgumentOutOfRangeException(nameof(intent));
        if (!Enum.IsDefined(purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose));

        _candidateKey = candidateKey;
        _fragment = fragment;
        _bounds = bounds;
        _coverage = coverage;
        _densityBits = BitConverter.SingleToInt32Bits(density);
        _format = format;
        _intent = intent;
        _purpose = purpose;
        _deviceContext = deviceContext;
        _deviceGridOffset = deviceGridOffset;
    }

    public object CandidateKey => _candidateKey;

    public Rect Bounds => _bounds;

    public RequiredRegion Coverage => _coverage;

    public float Density => BitConverter.Int32BitsToSingle(_densityBits);

    public RenderCacheFormatIdentity Format => _format;

    public RenderIntent Intent => _intent;

    public RenderRequestPurpose Purpose => _purpose;

    public RenderCacheDeviceContextIdentity DeviceContext => _deviceContext;

    public Vector DeviceGridOffset => _deviceGridOffset;

    public bool Equals(RenderOutputCacheIdentity? other)
        => other is not null
           && Equals(_candidateKey, other._candidateKey)
           && _fragment.Equals(other._fragment)
           && _bounds.Equals(other._bounds)
           && _coverage.Equals(other._coverage)
           && _densityBits == other._densityBits
           && _format.Equals(other._format)
           && _intent == other._intent
           && _purpose == other._purpose
           && _deviceContext.Equals(other._deviceContext)
           && _deviceGridOffset.Equals(other._deviceGridOffset);

    public override bool Equals(object? obj)
        => obj is RenderOutputCacheIdentity other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(
            _candidateKey,
            _fragment,
            _bounds,
            _coverage,
            _densityBits,
            _format,
            HashCode.Combine(_intent, _purpose, _deviceContext, _deviceGridOffset));
}

/// <summary>
/// An acquired cache entry. Payload ownership remains defined by the lookup implementation; the resolver only
/// retains this opaque handle and never reads or disposes the payload.
/// </summary>
internal sealed class RenderCacheEntry
{
    public RenderCacheEntry(RenderOutputCacheIdentity identity, object payload)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(payload);
        Identity = identity;
        Payload = payload;
    }

    public RenderOutputCacheIdentity Identity { get; }

    public object Payload { get; }
}

internal interface IRenderCacheLookup
{
    /// <remarks>
    /// One resolver call observes a stable lookup snapshot. Implementations must not change the result for the
    /// same candidate and complete identity until that call returns.
    /// </remarks>
    bool TryGet(
        RenderCacheCandidate candidate,
        RenderOutputCacheIdentity identity,
        out RenderCacheEntry? entry);
}

internal sealed class RenderNodeCacheLookup : IRenderCacheLookup
{
    public static RenderNodeCacheLookup Instance { get; } = new();

    private RenderNodeCacheLookup()
    {
    }

    public bool TryGet(
        RenderCacheCandidate candidate,
        RenderOutputCacheIdentity identity,
        out RenderCacheEntry? entry)
    {
        if (candidate.Cache?.TryGetCachedOutput(identity, out RenderNodeCachedOutput? output) == true)
        {
            entry = new RenderCacheEntry(identity, output!);
            return true;
        }

        entry = null;
        return false;
    }
}

internal enum RenderCacheResolutionKind : byte
{
    Bypass,
    Hit,
    MissCapture,
    Superseded,
}

internal enum RenderCacheBypassReason : byte
{
    None,
    CacheDisabled,
    MetadataOnlyPurpose,
    PersistentLookupDisabled,
    CapturePublicationDisabled,
    EmptyRequirement,
    OutsideCacheRules,
    ExternalInputExceedsBufferBudget,
    TargetTokenDependency,
    RawTargetWork,
    DeviceGridDependentOutput,
    NotMaterializable,
    UnstableBoundaryPlan,
}

internal sealed record RenderCacheHitSubstitution(
    RenderCacheCandidateId CandidateId,
    RenderFragmentId OriginalProducerId,
    ImmutableArray<RenderValueId> OriginalValueIds,
    RenderProvenanceId ProvenanceId,
    RenderOutputCacheIdentity Identity,
    RenderCacheEntry Entry);

/// <summary>
/// Describes a capture to insert immediately after the original producer. The executor keeps the actual payload
/// request-owned and unpublished; this descriptor becomes publishable only after complete-request success.
/// </summary>
internal sealed record RenderCacheMissCapture(
    RenderCacheCandidateId CandidateId,
    RenderFragmentId ProducerId,
    ImmutableArray<RenderValueId> ValueIds,
    RenderProvenanceId ProvenanceId,
    RenderOutputCacheIdentity Identity);

internal sealed record RenderCacheDecision(
    RenderCacheCandidate Candidate,
    RenderCacheResolutionKind Kind,
    RenderCacheBypassReason BypassReason,
    RenderOutputCacheIdentity? Identity,
    RenderCacheHitSubstitution? Hit,
    RenderCacheMissCapture? MissCapture,
    RenderCacheCandidateId? SupersededBy);

internal static class RenderMaterializationDensityPolicy
{
    public static float Clamp(
        RenderFragmentReference fragment,
        float density)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        if (fragment.Kind is RenderFragmentKind.MaterializedInput
            or RenderFragmentKind.BuiltInBackdropCapture)
        {
            return density;
        }
        if (fragment.Kind == RenderFragmentKind.ContributeValues
            && fragment.Inputs.Length == 1)
        {
            return Clamp(fragment.Inputs[0], density);
        }

        Rect logicalBounds = fragment.Kind == RenderFragmentKind.Layer
                             && fragment.Payload is LayerRenderFragmentPayload layer
            ? layer.Domain ?? fragment.Bounds
            : fragment.Bounds;
        return RequiresRasterApron(fragment)
            ? RenderScaleUtilities.ClampWorkingScaleToRasterApronBudget(logicalBounds, density)
            : RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(logicalBounds, density);
    }

    private static bool RequiresRasterApron(RenderFragmentReference fragment)
    {
        if (fragment.Kind == RenderFragmentKind.OpaqueSource
            && fragment.Payload is OpaqueRenderFragmentPayload opaque)
        {
            return opaque.Description.DirectReplay is not null;
        }

        return fragment.Kind == RenderFragmentKind.TargetScope
               && fragment.Payload is TargetScopeRenderFragmentPayload targetScope
               && targetScope.Description.IsValueReplayMap;
    }
}

internal static class RenderMaterializationDemandResolver
{
    private enum DemandUse : byte
    {
        ReplayTarget,
        MaterializeValue,
    }

    public static IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> Resolve(
        IReadOnlyList<RenderFragmentReference> roots,
        float outputScale,
        float maxWorkingScale,
        IReadOnlySet<RenderFragmentReference>? cacheBoundaries = null)
    {
        ArgumentNullException.ThrowIfNull(roots);
        if (!float.IsFinite(outputScale) || outputScale <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outputScale),
                outputScale,
                "The output density must be finite and positive.");
        }

        var result = new Dictionary<RenderFragmentReference, EffectiveScale>(
            ReferenceEqualityComparer.Instance);
        var replayDemands = new Dictionary<RenderFragmentReference, float>(
            ReferenceEqualityComparer.Instance);
        var materializedDemands = new Dictionary<RenderFragmentReference, float>(
            ReferenceEqualityComparer.Instance);
        var materializedUses = new HashSet<RenderFragmentReference>(
            ReferenceEqualityComparer.Instance);
        var pending = new Stack<(
            RenderFragmentReference Fragment,
            float Demand,
            DemandUse Use,
            bool UseSupplyFallback)>();
        float rootDemand = MathF.Min(
            outputScale,
            RenderScaleUtilities.SanitizeMaxWorkingScale(maxWorkingScale));
        for (int index = roots.Count - 1; index >= 0; index--)
        {
            pending.Push((
                roots[index],
                rootDemand,
                DemandUse.ReplayTarget,
                UseSupplyFallback: false));
        }

        while (pending.TryPop(out var item))
        {
            RenderFragmentReference fragment = item.Fragment;
            if (item.Use == DemandUse.ReplayTarget
                && cacheBoundaries?.Contains(fragment) == true)
            {
                pending.Push((
                    fragment,
                    item.Demand,
                    DemandUse.MaterializeValue,
                    item.UseSupplyFallback));
                continue;
            }

            float demand = ResolveDemand(
                fragment,
                item.Demand,
                item.UseSupplyFallback,
                maxWorkingScale);
            bool outputDemandChanged = MergeDemand(result, fragment, demand);
            if (outputDemandChanged && materializedUses.Contains(fragment))
            {
                pending.Push((
                    fragment,
                    demand,
                    DemandUse.MaterializeValue,
                    UseSupplyFallback: false));
            }

            if (item.Use == DemandUse.MaterializeValue)
            {
                materializedUses.Add(fragment);
                float selectedDemand = result[fragment].Value;
                if (!MergeProcessedDemand(materializedDemands, fragment, selectedDemand))
                    continue;

                EnqueueMaterializedInputs(fragment, selectedDemand, pending);
                continue;
            }

            if (!MergeProcessedDemand(replayDemands, fragment, item.Demand))
                continue;

            EnqueueReplayInputs(fragment, item.Demand, pending);
        }

        return result;
    }

    private static float ResolveDemand(
        RenderFragmentReference fragment,
        float requestedDemand,
        bool useSupplyFallback,
        float maxWorkingScale)
    {
        if (!fragment.EffectiveScale.IsUnbounded)
            return fragment.EffectiveScale.Value;

        float demand = requestedDemand;
        // A target command does not provide a caller density. Preserve the legacy
        // Layer contract by negotiating from its densest concrete child supply.
        if (useSupplyFallback && fragment.Kind == RenderFragmentKind.Layer)
        {
            foreach (RenderFragmentReference input in fragment.Inputs)
            {
                if (!input.EffectiveScale.IsUnbounded)
                    demand = MathF.Max(demand, input.EffectiveScale.Value);
            }
        }

        demand = MathF.Min(
            demand,
            RenderScaleUtilities.SanitizeMaxWorkingScale(maxWorkingScale));
        return RenderMaterializationDensityPolicy.Clamp(
            fragment,
            demand);
    }

    private static bool MergeDemand(
        IDictionary<RenderFragmentReference, EffectiveScale> demands,
        RenderFragmentReference fragment,
        float demand)
    {
        if (demands.TryGetValue(fragment, out EffectiveScale existing)
            && existing.Value >= demand)
        {
            return false;
        }

        demands[fragment] = EffectiveScale.At(demand);
        return true;
    }

    private static bool MergeProcessedDemand(
        IDictionary<RenderFragmentReference, float> demands,
        RenderFragmentReference fragment,
        float demand)
    {
        if (demands.TryGetValue(fragment, out float existing) && existing >= demand)
            return false;

        demands[fragment] = demand;
        return true;
    }

    private static void EnqueueReplayInputs(
        RenderFragmentReference fragment,
        float targetDemand,
        Stack<(
            RenderFragmentReference Fragment,
            float Demand,
            DemandUse Use,
            bool UseSupplyFallback)> pending)
    {
        switch (fragment.Kind)
        {
            case RenderFragmentKind.Opacity:
            case RenderFragmentKind.Blend:
            case RenderFragmentKind.TargetLayerScope:
            case RenderFragmentKind.TargetScope:
            case RenderFragmentKind.RawTargetScope:
                EnqueueInputs(fragment, targetDemand, DemandUse.ReplayTarget, pending);
                return;
            case RenderFragmentKind.OpacityMask:
                if (fragment.Inputs.Length > 0)
                {
                    for (int index = fragment.Inputs.Length - 1; index >= 1; index--)
                    {
                        pending.Push((
                            fragment.Inputs[index],
                            targetDemand,
                            DemandUse.MaterializeValue,
                            UseSupplyFallback: false));
                    }

                    pending.Push((
                        fragment.Inputs[0],
                        targetDemand,
                        DemandUse.ReplayTarget,
                        UseSupplyFallback: false));
                }
                return;
            case RenderFragmentKind.TargetCommand:
                for (int index = fragment.Inputs.Length - 1; index >= 0; index--)
                {
                    pending.Push((
                        fragment.Inputs[index],
                        targetDemand,
                        DemandUse.MaterializeValue,
                        UseSupplyFallback: true));
                }
                return;
            case RenderFragmentKind.RawTargetCommand:
                return;
            case RenderFragmentKind.ContributeValues:
                EnqueueInputs(fragment, targetDemand, DemandUse.MaterializeValue, pending);
                return;
            default:
                pending.Push((
                    fragment,
                    targetDemand,
                    DemandUse.MaterializeValue,
                    UseSupplyFallback: false));
                return;
        }
    }

    private static void EnqueueMaterializedInputs(
        RenderFragmentReference fragment,
        float valueDemand,
        Stack<(
            RenderFragmentReference Fragment,
            float Demand,
            DemandUse Use,
            bool UseSupplyFallback)> pending)
    {
        switch (fragment.Kind)
        {
            case RenderFragmentKind.Layer:
            case RenderFragmentKind.TargetScope:
                EnqueueInputs(fragment, valueDemand, DemandUse.ReplayTarget, pending);
                return;
            case RenderFragmentKind.MaterializedInput:
            case RenderFragmentKind.TargetCapture:
            case RenderFragmentKind.BuiltInBackdropCapture:
                return;
            default:
                EnqueueInputs(fragment, valueDemand, DemandUse.MaterializeValue, pending);
                return;
        }
    }

    private static void EnqueueInputs(
        RenderFragmentReference fragment,
        float demand,
        DemandUse use,
        Stack<(
            RenderFragmentReference Fragment,
            float Demand,
            DemandUse Use,
            bool UseSupplyFallback)> pending)
    {
        for (int index = fragment.Inputs.Length - 1; index >= 0; index--)
        {
            pending.Push((
                fragment.Inputs[index],
                demand,
                use,
                UseSupplyFallback: false));
        }
    }
}

internal sealed class RenderCacheResolution
{
    public RenderCacheResolution(ImmutableArray<RenderCacheDecision> decisions)
    {
        Decisions = decisions;
        Hits = [.. decisions
            .Where(static item => item.Hit is not null)
            .Select(static item => item.Hit!)];
        MissCaptures = [.. decisions
            .Where(static item => item.MissCapture is not null)
            .Select(static item => item.MissCapture!)];
        BoundaryFragmentIds = [.. Hits
            .Select(static item => item.OriginalProducerId)
            .Concat(MissCaptures.Select(static item => item.ProducerId))
            .Distinct()];
    }

    public ImmutableArray<RenderCacheDecision> Decisions { get; }

    public ImmutableArray<RenderCacheHitSubstitution> Hits { get; }

    public ImmutableArray<RenderCacheMissCapture> MissCaptures { get; }

    public ImmutableArray<RenderFragmentId> BoundaryFragmentIds { get; }

    public RenderCacheDecision GetDecision(RenderCacheCandidateId id)
        => Decisions.FirstOrDefault(item => item.Candidate.Id == id)
           ?? throw new KeyNotFoundException("The cache candidate is not part of this resolution.");
}

internal sealed record RenderCachePlanningResult(
    RenderCacheResolution Resolution,
    IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> MaterializationDemands,
    int ResolutionPasses);

/// <summary>
/// Resolves cache candidates only after target dependencies, metadata, and required regions are known. It does
/// not mutate the recorded graph: substitutions and capture points refer back to the original producer/value and
/// provenance IDs, leaving every fragment input and target-token edge intact.
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
        IReadOnlyDictionary<RenderFragmentReference, EffectiveScale>? uncachedDemands = null;
        // Selecting a hit or miss changes a fragment from target replay to value
        // materialization, which can change descendant density and therefore identity.
        // Resolve every candidate independently while finding the fixed point. Parent-hit
        // supersedence is an execution selection and must not remove a child from density
        // planning before the ancestor identity that selected the hit is stable.
        for (int pass = 1; pass <= MaximumResolutionPasses; pass++)
        {
            IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> demands =
                RenderMaterializationDemandResolver.Resolve(
                    roots,
                    request.Options.OutputScale,
                    request.Options.MaxWorkingScale,
                    planningBoundaries);
            uncachedDemands ??= demands;
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
                    pass);
            }

            if (!visitedBoundarySets.Add(nextPlanningBoundaries))
            {
                return CreateUnstableBoundaryFallback(
                    graph,
                    uncachedDemands,
                    pass);
            }

            planningBoundaries = nextPlanningBoundaries;
        }

        return CreateUnstableBoundaryFallback(
            graph,
            uncachedDemands!,
            MaximumResolutionPasses);
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
        Dictionary<RenderFragmentReference, RenderFragmentOutputIdentity>? identityMemo =
            context.AllowCapturePublication
                ? null
                : new Dictionary<RenderFragmentReference, RenderFragmentOutputIdentity>(
                    ReferenceEqualityComparer.Instance);
        foreach (RenderCacheCandidate candidate in index.Graph.CacheCandidates)
        {
            RecordedRenderFragment recorded = index.Fragments[candidate.FragmentId];
            RenderFragmentReference reference = index.References[candidate.FragmentId];
            CandidateEvaluation evaluation = EvaluateCandidate(
                request,
                reference,
                recorded,
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
        var identityMemo = new Dictionary<RenderFragmentReference, RenderFragmentOutputIdentity>(
            ReferenceEqualityComparer.Instance);
        var decisions = new Dictionary<RenderCacheCandidateId, RenderCacheDecision>();
        var selectedHits = new List<RenderCacheCandidateId>();
        foreach (RenderCacheCandidate candidate in candidates)
        {
            if (topology is not null)
            {
                RenderCacheCandidateId superseding = selectedHits
                    .FirstOrDefault(parent => topology.Descendants[parent].Contains(candidate.Id));
                if (superseding.Value > 0)
                {
                    decisions.Add(
                        candidate.Id,
                        Superseded(candidate, superseding));
                    continue;
                }
            }

            RenderCacheDecision decision = ResolveCandidate(
                request,
                candidate,
                index.Fragments[candidate.FragmentId],
                index.References[candidate.FragmentId],
                regions,
                context,
                materializationDemands,
                lookupMemo,
                identityMemo,
                index.DeviceGridAffectedReferences,
                index.TransformDependentReferences);
            decisions.Add(candidate.Id, decision);
            if (decision.Kind == RenderCacheResolutionKind.Hit)
                selectedHits.Add(candidate.Id);
        }

        return new RenderCacheResolution(
            [.. index.Graph.CacheCandidates.Select(candidate => decisions[candidate.Id])]);
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
        IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> uncachedDemands,
        int resolutionPasses)
    {
        var resolution = new RenderCacheResolution(
            [.. graph.CacheCandidates.Select(candidate =>
                Bypass(candidate, RenderCacheBypassReason.UnstableBoundaryPlan))]);
        return new RenderCachePlanningResult(
            resolution,
            uncachedDemands,
            resolutionPasses);
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
            null,
            superseding);

    private static RenderCacheDecision ResolveCandidate(
        RenderRequest request,
        RenderCacheCandidate candidate,
        RecordedRenderFragment recorded,
        RenderFragmentReference reference,
        RegionAnalysis regions,
        RenderCacheResolutionContext context,
        IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> materializationDemands,
        LookupMemo lookupMemo,
        IDictionary<RenderFragmentReference, RenderFragmentOutputIdentity> identityMemo,
        IReadOnlySet<RenderFragmentReference> deviceGridAffectedReferences,
        IReadOnlySet<RenderFragmentReference> transformDependentReferences)
    {
        CandidateEvaluation evaluation = EvaluateCandidate(
            request,
            reference,
            recorded,
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
                identity,
                new RenderCacheHitSubstitution(
                    candidate.Id,
                    recorded.Id,
                    recorded.Values,
                    recorded.ProvenanceId,
                    identity,
                    entry!),
                null,
                null);
        }

        if (!context.AllowCapturePublication)
            return Bypass(candidate, RenderCacheBypassReason.CapturePublicationDisabled, identity);

        return new RenderCacheDecision(
            candidate,
            RenderCacheResolutionKind.MissCapture,
            RenderCacheBypassReason.None,
            identity,
            null,
            new RenderCacheMissCapture(
                candidate.Id,
                recorded.Id,
                recorded.Values,
                recorded.ProvenanceId,
                identity),
                null);
    }

    private static RenderOutputCacheIdentity CreateIdentity(
        RenderRequest request,
        RenderCacheCandidate candidate,
        RenderFragmentReference reference,
        CandidateEvaluation evaluation,
        RenderCacheResolutionContext context,
        IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> materializationDemands,
        IDictionary<RenderFragmentReference, RenderFragmentOutputIdentity> identityMemo)
        => new(
            candidate.CacheKey,
            RenderFragmentOutputIdentity.Create(
                reference,
                graphRequestId: request.Id,
                materializationDemands,
                identityMemo),
            evaluation.Metadata.Bounds,
            evaluation.Coverage,
            evaluation.Density,
            context.Format,
            request.Options.Intent,
            request.Options.Purpose,
            context.DeviceContext,
            evaluation.DeviceGridOffset);

    private static CandidateEvaluation EvaluateCandidate(
        RenderRequest request,
        RenderFragmentReference reference,
        RecordedRenderFragment recorded,
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
        if (!reference.CanBeUsedAsValueInput || recorded.Values.IsDefaultOrEmpty)
            return CandidateEvaluation.Bypass(RenderCacheBypassReason.NotMaterializable);
        if (reference.Kind == RenderFragmentKind.MaterializedInput
            && reference.Payload is MaterializedInputRenderFragmentPayload input
            && (input.Description.DeviceBounds.Width > RenderScaleUtilities.MaxBufferDimension
                || input.Description.DeviceBounds.Height > RenderScaleUtilities.MaxBufferDimension))
        {
            return CandidateEvaluation.Bypass(
                RenderCacheBypassReason.ExternalInputExceedsBufferBudget);
        }

        if (!regions.FragmentRequirements.TryGetValue(recorded.Id, out RequiredRegion requirement)
            || !regions.Metadata.TryGetValue(recorded.Id, out ResolvedFragmentMetadata metadata))
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
            && opaque.Description.DirectReplay is not null;
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
                or RenderFragmentKind.LegacyFilterEffect)
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
        RenderCacheBypassReason reason,
        RenderOutputCacheIdentity? identity = null)
        => new(
            candidate,
            RenderCacheResolutionKind.Bypass,
            reason,
            identity,
            null,
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
            Fragments = new Dictionary<RenderFragmentId, RecordedRenderFragment>(
                graph.Fragments.Length);
            References = new Dictionary<RenderFragmentId, RenderFragmentReference>(
                graph.Fragments.Length);
            foreach (RecordedRenderFragment fragment in graph.Fragments)
            {
                if (fragment.Payload is not RenderFragmentReference reference)
                {
                    throw new InvalidOperationException(
                        "A cache-planning fragment is missing its semantic reference.");
                }

                Fragments.Add(fragment.Id, fragment);
                References.Add(fragment.Id, reference);
            }

            var deviceGridReferences = ResolveDeviceGridReferences(References.Values);
            DeviceGridAffectedReferences = deviceGridReferences.Affected;
            TransformDependentReferences = deviceGridReferences.TransformDependent;
        }

        public RecordedRenderGraph Graph { get; }

        public Dictionary<RenderFragmentId, RecordedRenderFragment> Fragments { get; }

        public Dictionary<RenderFragmentId, RenderFragmentReference> References { get; }

        public HashSet<RenderFragmentReference> DeviceGridAffectedReferences { get; }

        public HashSet<RenderFragmentReference> TransformDependentReferences { get; }

        public CandidateTopology GetTopology()
            => _topology ??= BuildCandidateTopology(Graph, References);

        private static (
            HashSet<RenderFragmentReference> Affected,
            HashSet<RenderFragmentReference> TransformDependent) ResolveDeviceGridReferences(
                IEnumerable<RenderFragmentReference> references)
        {
            RenderFragmentReference[] all = references.ToArray();
            var consumers = new Dictionary<RenderFragmentReference, List<RenderFragmentReference>>(
                ReferenceEqualityComparer.Instance);
            foreach (RenderFragmentReference reference in all)
                consumers.Add(reference, []);
            foreach (RenderFragmentReference reference in all)
            {
                foreach (RenderFragmentReference input in reference.Inputs)
                    consumers[input].Add(reference);
            }

            RenderFragmentReference[] phaseUnsafeMaskScopes =
            [
                .. all.Where(IsPhaseUnsafeMaskScope),
            ];
            RenderFragmentReference[] sensitive =
            [
                .. all.Where(IsDeviceGridSensitive),
                .. phaseUnsafeMaskScopes,
            ];
            HashSet<RenderFragmentReference> affected = ExpandConnectedReferences(
                sensitive,
                consumers);
            RenderFragmentReference[] transformRoots =
            [
                .. sensitive.Where(reference => HasNonIdentityValueReplayAncestor(reference, consumers)),
            ];
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
            if (reference.Kind is RenderFragmentKind.LegacyFilterEffect
                or RenderFragmentKind.Shader
                or RenderFragmentKind.Geometry)
            {
                return true;
            }

            if (reference.Payload is OpaqueRenderFragmentPayload opaque)
            {
                bool isText = opaque.Description.StructuralKey is Type type
                              && type == typeof(TextRenderNode);
                bool isDrawableBrushHost = reference.Kind == RenderFragmentKind.OpaqueCombine
                                           && opaque.Description.DirectReplay is not null;
                if (isText || isDrawableBrushHost)
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

        private static bool HasNonIdentityValueReplayAncestor(
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
                if (current.Kind == RenderFragmentKind.TargetScope
                    && current.Payload is TargetScopeRenderFragmentPayload scope
                    && scope.Description.IsValueReplayMap
                    && IsNonIdentityTransform(scope.Description.RuntimeIdentity?.Key))
                {
                    return true;
                }

                foreach (RenderFragmentReference consumer in consumers[current])
                    pending.Push(consumer);
            }

            return false;
        }

        private static bool IsNonIdentityTransform(object? runtimeIdentity)
        {
            return runtimeIdentity switch
            {
                Matrix matrix => !matrix.IsIdentity,
                ValueTuple<Matrix, TransformOperator> tuple
                    when tuple.Item2 == TransformOperator.Prepend => !tuple.Item1.IsIdentity,
                _ => false,
            };
        }
    }

    private sealed record CandidateTopology(
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

    private static CandidateTopology BuildCandidateTopology(
        RecordedRenderGraph graph,
        IReadOnlyDictionary<RenderFragmentId, RenderFragmentReference> references)
    {
        var result = new Dictionary<RenderCacheCandidateId, HashSet<RenderCacheCandidateId>>();
        foreach (RenderCacheCandidate parent in graph.CacheCandidates)
        {
            var descendants = new HashSet<RenderCacheCandidateId>();
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

                if (DependsOn(references[parent.FragmentId], references[child.FragmentId]))
                    descendants.Add(child.Id);
            }
            result.Add(parent.Id, descendants);
        }
        RenderCacheCandidate[] parentFirst = [.. graph.CacheCandidates
            .OrderByDescending(candidate => result[candidate.Id].Count)
            .ThenByDescending(static candidate => candidate.AuthoredOrder)];
        return new CandidateTopology(result, parentFirst);
    }

    private static bool DependsOn(
        RenderFragmentReference parent,
        RenderFragmentReference possibleDescendant)
    {
        var visited = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<RenderFragmentReference>(parent.Inputs);
        while (pending.TryPop(out RenderFragmentReference? current))
        {
            if (ReferenceEquals(current, possibleDescendant))
                return true;
            if (!visited.Add(current))
                continue;
            foreach (RenderFragmentReference input in current.Inputs)
                pending.Push(input);
        }
        return false;
    }
}

internal sealed class RenderFragmentOutputIdentity : IEquatable<RenderFragmentOutputIdentity>
{
    private readonly RenderFragmentKind _kind;
    private readonly Rect _bounds;
    private readonly int? _scaleBits;
    private readonly int? _materializationScaleBits;
    private readonly RenderValueCardinality _cardinality;
    private readonly bool _contributes;
    private readonly object[] _runtimeComponents;
    private readonly RenderFragmentOutputIdentity[] _inputs;

    private RenderFragmentOutputIdentity(
        RenderFragmentReference reference,
        EffectiveScale? materializationDemand,
        object[] runtimeComponents,
        RenderFragmentOutputIdentity[] inputs)
    {
        _kind = reference.Kind;
        _bounds = reference.Bounds;
        _scaleBits = reference.EffectiveScale.IsUnbounded
            ? null
            : BitConverter.SingleToInt32Bits(reference.EffectiveScale.Value);
        _materializationScaleBits = materializationDemand is { } demand
            ? BitConverter.SingleToInt32Bits(demand.Value)
            : null;
        _cardinality = reference.ValueCardinality;
        _contributes = reference.ContributesValuesToTarget;
        _runtimeComponents = runtimeComponents;
        _inputs = inputs;
    }

    public static RenderFragmentOutputIdentity Create(
        RenderFragmentReference reference,
        RenderRequestId graphRequestId,
        IReadOnlyDictionary<RenderFragmentReference, EffectiveScale>? materializationDemands = null)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var memo = new Dictionary<RenderFragmentReference, RenderFragmentOutputIdentity>(
            ReferenceEqualityComparer.Instance);
        return CreateCore(reference, graphRequestId, materializationDemands, memo);
    }

    internal static RenderFragmentOutputIdentity Create(
        RenderFragmentReference reference,
        RenderRequestId graphRequestId,
        IReadOnlyDictionary<RenderFragmentReference, EffectiveScale>? materializationDemands,
        IDictionary<RenderFragmentReference, RenderFragmentOutputIdentity> memo)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(memo);
        return CreateCore(reference, graphRequestId, materializationDemands, memo);
    }

    public bool Equals(RenderFragmentOutputIdentity? other)
    {
        if (other is null
            || _kind != other._kind
            || !_bounds.Equals(other._bounds)
            || _scaleBits != other._scaleBits
            || _materializationScaleBits != other._materializationScaleBits
            || !_cardinality.Equals(other._cardinality)
            || _contributes != other._contributes
            || _runtimeComponents.Length != other._runtimeComponents.Length
            || _inputs.Length != other._inputs.Length)
        {
            return false;
        }

        for (int index = 0; index < _runtimeComponents.Length; index++)
        {
            if (!Equals(_runtimeComponents[index], other._runtimeComponents[index]))
                return false;
        }
        return _inputs.AsSpan().SequenceEqual(other._inputs);
    }

    public override bool Equals(object? obj)
        => obj is RenderFragmentOutputIdentity other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_kind);
        hash.Add(_bounds);
        hash.Add(_scaleBits);
        hash.Add(_materializationScaleBits);
        hash.Add(_cardinality);
        hash.Add(_contributes);
        foreach (object component in _runtimeComponents)
            hash.Add(component);
        foreach (RenderFragmentOutputIdentity input in _inputs)
            hash.Add(input);
        return hash.ToHashCode();
    }

    private static RenderFragmentOutputIdentity CreateCore(
        RenderFragmentReference reference,
        RenderRequestId requestId,
        IReadOnlyDictionary<RenderFragmentReference, EffectiveScale>? materializationDemands,
        IDictionary<RenderFragmentReference, RenderFragmentOutputIdentity> memo)
    {
        if (memo.TryGetValue(reference, out RenderFragmentOutputIdentity? cached))
            return cached;

        RenderFragmentOutputIdentity[] inputs = reference.Inputs
            .Select(input => CreateCore(input, requestId, materializationDemands, memo))
            .ToArray();
        var components = new List<object>();
        AddRuntimeComponents(reference, requestId, components);
        EffectiveScale? demand = materializationDemands?.TryGetValue(
            reference,
            out EffectiveScale selectedDemand) == true
            ? selectedDemand
            : null;
        var identity = new RenderFragmentOutputIdentity(
            reference,
            demand,
            components.ToArray(),
            inputs);
        memo.Add(reference, identity);
        return identity;
    }

    private static void AddRuntimeComponents(
        RenderFragmentReference reference,
        RenderRequestId requestId,
        ICollection<object> components)
    {
        switch (reference.Payload)
        {
            case null:
                return;
            case OpacityRenderFragmentPayload opacity:
                components.Add(BitConverter.SingleToInt32Bits(opacity.Opacity));
                return;
            case BlendRenderFragmentPayload blend:
                components.Add(blend.BlendMode);
                return;
            case OpacityMaskRenderFragmentPayload mask:
                components.Add(mask.Mask.Kind);
                components.Add(mask.Mask.DependencyIndex);
                components.Add(mask.BrushBounds);
                components.Add(mask.Invert);
                AddResources(mask.Resources, components);
                return;
            case ShaderRenderFragmentPayload shader:
                components.Add(shader.Description.StructuralIdentity);
                components.Add(shader.RuntimeIdentity);
                return;
            case GeometryRenderFragmentPayload geometry:
                components.Add(geometry.Description.StructuralIdentity);
                components.Add(geometry.RuntimeIdentity);
                AddResources(geometry.Description.Resources, components);
                return;
            case LayerRenderFragmentPayload layer:
                components.Add(layer.Domain.HasValue);
                if (layer.Domain is { } layerDomain)
                    components.Add(layerDomain);
                return;
            case TargetLayerScopeRenderFragmentPayload layer:
                components.Add(layer.Region);
                return;
            case OpaqueRenderFragmentPayload opaque:
                components.Add(opaque.Topology);
                components.Add(opaque.Description.RuntimeIdentity?.Key
                               ?? RequestLocalIdentity(reference, requestId, "opaque"));
                AddResources(opaque.Description.Resources, components);
                return;
            case LegacyFilterEffectRenderFragmentPayload legacy:
                components.Add(legacy.Context.CacheIdentity);
                return;
            case MaterializedInputRenderFragmentPayload input:
                components.Add(input.Description.Target.CacheIdentity);
                return;
            case TargetCaptureRenderFragmentPayload capture:
                components.Add(capture.Description.SourceRegion);
                components.Add(capture.Description.Bounds);
                return;
            case BuiltInBackdropCaptureRenderFragmentPayload capture:
                components.Add(capture.Description.SourceRegion);
                components.Add(capture.Description.Bounds);
                components.Add(RequestLocalIdentity(reference, requestId, "backdrop"));
                return;
            case TargetScopeRenderFragmentPayload scope:
                components.Add(scope.Description.RuntimeIdentity?.Key
                               ?? RequestLocalIdentity(reference, requestId, "target-scope"));
                AddResources(scope.Description.Resources, components);
                return;
            case RawTargetScopeRenderFragmentPayload:
            case RawTargetCommandRenderFragmentPayload:
                components.Add(RequestLocalIdentity(reference, requestId, "raw-target"));
                return;
            case TargetCommandRenderFragmentPayload command:
                components.Add(command.Description.AffectedRegion);
                components.Add(command.Description.Access);
                components.Add(command.Description.RuntimeIdentity?.Key
                               ?? RequestLocalIdentity(reference, requestId, "target-command"));
                AddResources(command.Description.Resources, components);
                return;
            default:
                components.Add(reference.Payload.GetType());
                components.Add(reference.Payload);
                return;
        }
    }

    private static object RequestLocalIdentity(
        RenderFragmentReference reference,
        RenderRequestId requestId,
        string role)
        => new RequestLocalRenderCacheIdentity(
            requestId.Value,
            reference.Id?.Value ?? 0,
            role);

    private static void AddResources(
        IReadOnlyList<RenderResource> resources,
        ICollection<object> components)
    {
        components.Add(resources.Count);
        foreach (RenderResource resource in resources)
            components.Add(resource.CacheIdentity);
    }

    private sealed record RequestLocalRenderCacheIdentity(
        long RequestId,
        long FragmentId,
        string Role);
}
