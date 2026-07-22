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
        bool allowCapturePublication = true)
    {
        format.ThrowIfUninitialized(nameof(format));
        deviceContext.ThrowIfUninitialized(nameof(deviceContext));
        Format = format;
        DeviceContext = deviceContext;
        AllowPersistentLookup = allowPersistentLookup;
        AllowCapturePublication = allowCapturePublication;
    }

    public RenderCacheFormatIdentity Format { get; }

    public RenderCacheDeviceContextIdentity DeviceContext { get; }

    public bool AllowPersistentLookup { get; }

    public bool AllowCapturePublication { get; }
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

    public RenderOutputCacheIdentity(
        object candidateKey,
        RenderFragmentOutputIdentity fragment,
        Rect bounds,
        RequiredRegion coverage,
        float density,
        RenderCacheFormatIdentity format,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        RenderCacheDeviceContextIdentity deviceContext)
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
    }

    public object CandidateKey => _candidateKey;

    public Rect Bounds => _bounds;

    public RequiredRegion Coverage => _coverage;

    public float Density => BitConverter.Int32BitsToSingle(_densityBits);

    public RenderCacheFormatIdentity Format => _format;

    public RenderIntent Intent => _intent;

    public RenderRequestPurpose Purpose => _purpose;

    public RenderCacheDeviceContextIdentity DeviceContext => _deviceContext;

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
           && _deviceContext.Equals(other._deviceContext);

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
            HashCode.Combine(_intent, _purpose, _deviceContext));
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
    TargetTokenDependency,
    RawTargetWork,
    NotMaterializable,
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

/// <summary>
/// Resolves cache candidates only after target dependencies, metadata, and required regions are known. It does
/// not mutate the recorded graph: substitutions and capture points refer back to the original producer/value and
/// provenance IDs, leaving every fragment input and target-token edge intact.
/// </summary>
internal sealed class RenderCacheResolver
{
    public RenderCacheResolution Resolve(
        RenderRequest request,
        RecordedRenderGraph graph,
        RegionAnalysis regions,
        RenderCacheResolutionContext context,
        IRenderCacheLookup? lookup = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(regions);
        context.Format.ThrowIfUninitialized(nameof(context));
        context.DeviceContext.ThrowIfUninitialized(nameof(context));
        if (request.Id != graph.RequestId)
            throw new ArgumentException("The recorded graph belongs to a different render request.", nameof(graph));
        if (request.State != RenderRequestState.RegionsResolved)
        {
            throw new InvalidOperationException(
                "Render-cache resolution requires completed graph, target-dependency, metadata, and region discovery.");
        }

        Dictionary<RenderFragmentId, RecordedRenderFragment> fragments = graph.Fragments
            .ToDictionary(static item => item.Id);
        Dictionary<RenderCacheCandidateId, HashSet<RenderCacheCandidateId>> descendants =
            BuildCandidateDescendants(graph, fragments);
        RenderCacheCandidate[] parentFirst = [.. graph.CacheCandidates
            .OrderByDescending(candidate => descendants[candidate.Id].Count)
            .ThenByDescending(static candidate => candidate.AuthoredOrder)];

        var decisions = new Dictionary<RenderCacheCandidateId, RenderCacheDecision>();
        var selectedHits = new List<RenderCacheCandidateId>();
        foreach (RenderCacheCandidate candidate in parentFirst)
        {
            RenderCacheCandidateId superseding = selectedHits
                .FirstOrDefault(parent => descendants[parent].Contains(candidate.Id));
            if (superseding.Value > 0)
            {
                decisions.Add(
                    candidate.Id,
                    new RenderCacheDecision(
                        candidate,
                        RenderCacheResolutionKind.Superseded,
                        RenderCacheBypassReason.None,
                        null,
                        null,
                        null,
                        superseding));
                continue;
            }

            RenderCacheDecision decision = ResolveCandidate(
                request,
                candidate,
                fragments[candidate.FragmentId],
                regions,
                context,
                lookup);
            decisions.Add(candidate.Id, decision);
            if (decision.Kind == RenderCacheResolutionKind.Hit)
                selectedHits.Add(candidate.Id);
        }

        return new RenderCacheResolution(
            [.. graph.CacheCandidates.Select(candidate => decisions[candidate.Id])]);
    }

    private static RenderCacheDecision ResolveCandidate(
        RenderRequest request,
        RenderCacheCandidate candidate,
        RecordedRenderFragment recorded,
        RegionAnalysis regions,
        RenderCacheResolutionContext context,
        IRenderCacheLookup? lookup)
    {
        if (recorded.Payload is not RenderFragmentReference reference)
            throw new InvalidOperationException("A cache candidate is missing its semantic fragment reference.");

        RenderCacheBypassReason reason = GetBypassReason(request, reference, recorded, regions, context);
        if (reason != RenderCacheBypassReason.None)
            return Bypass(candidate, reason);

        RequiredRegion coverage = regions.FragmentRequirements[candidate.FragmentId];
        ResolvedFragmentMetadata metadata = regions.Metadata[candidate.FragmentId];
        float density = ResolveMaterializationDensity(request.Options, metadata);
        var identity = new RenderOutputCacheIdentity(
            candidate.CacheKey,
            RenderFragmentOutputIdentity.Create(reference, graphRequestId: request.Id),
            metadata.Bounds,
            coverage,
            density,
            context.Format,
            request.Options.Intent,
            request.Options.Purpose,
            context.DeviceContext);

        if (context.AllowPersistentLookup
            && lookup?.TryGet(candidate, identity, out RenderCacheEntry? entry) == true
            && entry is not null
            && entry.Identity.Equals(identity))
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
                    entry),
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

    private static RenderCacheBypassReason GetBypassReason(
        RenderRequest request,
        RenderFragmentReference reference,
        RecordedRenderFragment recorded,
        RegionAnalysis regions,
        RenderCacheResolutionContext context)
    {
        if (!request.Options.CachePolicy.IsEnabled)
            return RenderCacheBypassReason.CacheDisabled;
        if (request.Options.Purpose is RenderRequestPurpose.Bounds or RenderRequestPurpose.HitTest)
            return RenderCacheBypassReason.MetadataOnlyPurpose;
        if (!context.AllowPersistentLookup && !context.AllowCapturePublication)
            return RenderCacheBypassReason.PersistentLookupDisabled;
        if (ContainsRawTargetWork(reference))
            return RenderCacheBypassReason.RawTargetWork;
        if (RenderFragmentTargetDependency.HasExternalTargetDependency(reference))
            return RenderCacheBypassReason.TargetTokenDependency;
        if (!reference.CanBeUsedAsValueInput || recorded.Values.IsDefaultOrEmpty)
            return RenderCacheBypassReason.NotMaterializable;

        if (!regions.FragmentRequirements.TryGetValue(recorded.Id, out RequiredRegion requirement)
            || !regions.Metadata.TryGetValue(recorded.Id, out ResolvedFragmentMetadata metadata))
        {
            return RenderCacheBypassReason.EmptyRequirement;
        }
        if (requirement.IsEmpty)
            return RenderCacheBypassReason.EmptyRequirement;

        float density = ResolveMaterializationDensity(request.Options, metadata);
        Rect coverage = requirement.IsFull ? metadata.Bounds : requirement.Value;
        PixelRect deviceCoverage = PixelRect.FromRect(coverage, density);
        return request.Options.CachePolicy.Rules.Match(deviceCoverage.Size)
            ? RenderCacheBypassReason.None
            : RenderCacheBypassReason.OutsideCacheRules;
    }

    private static float ResolveMaterializationDensity(
        RenderRequestOptions options,
        ResolvedFragmentMetadata metadata)
    {
        float density = RenderScaleUtilities.ResolveWorkingScale(
            [metadata.EffectiveScale],
            options.OutputScale,
            options.MaxWorkingScale);
        return RenderScaleUtilities.ClampWorkingScaleToBufferBudget(metadata.Bounds, density);
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

    private static Dictionary<RenderCacheCandidateId, HashSet<RenderCacheCandidateId>> BuildCandidateDescendants(
        RecordedRenderGraph graph,
        IReadOnlyDictionary<RenderFragmentId, RecordedRenderFragment> fragments)
    {
        var references = new Dictionary<RenderFragmentId, RenderFragmentReference>();
        foreach ((RenderFragmentId id, RecordedRenderFragment fragment) in fragments)
        {
            if (fragment.Payload is not RenderFragmentReference reference)
                throw new InvalidOperationException("A cache candidate graph is missing a semantic fragment reference.");
            references.Add(id, reference);
        }

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
        return result;
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
    private readonly RenderValueCardinality _cardinality;
    private readonly bool _contributes;
    private readonly object[] _runtimeComponents;
    private readonly RenderFragmentOutputIdentity[] _inputs;

    private RenderFragmentOutputIdentity(
        RenderFragmentReference reference,
        object[] runtimeComponents,
        RenderFragmentOutputIdentity[] inputs)
    {
        _kind = reference.Kind;
        _bounds = reference.Bounds;
        _scaleBits = reference.EffectiveScale.IsUnbounded
            ? null
            : BitConverter.SingleToInt32Bits(reference.EffectiveScale.Value);
        _cardinality = reference.ValueCardinality;
        _contributes = reference.ContributesValuesToTarget;
        _runtimeComponents = runtimeComponents;
        _inputs = inputs;
    }

    public static RenderFragmentOutputIdentity Create(
        RenderFragmentReference reference,
        RenderRequestId graphRequestId)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var memo = new Dictionary<RenderFragmentReference, RenderFragmentOutputIdentity>(
            ReferenceEqualityComparer.Instance);
        return CreateCore(reference, graphRequestId, memo);
    }

    public bool Equals(RenderFragmentOutputIdentity? other)
    {
        if (other is null
            || _kind != other._kind
            || !_bounds.Equals(other._bounds)
            || _scaleBits != other._scaleBits
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
        IDictionary<RenderFragmentReference, RenderFragmentOutputIdentity> memo)
    {
        if (memo.TryGetValue(reference, out RenderFragmentOutputIdentity? cached))
            return cached;

        RenderFragmentOutputIdentity[] inputs = reference.Inputs
            .Select(input => CreateCore(input, requestId, memo))
            .ToArray();
        var components = new List<object>();
        AddRuntimeComponents(reference, requestId, components);
        var identity = new RenderFragmentOutputIdentity(reference, components.ToArray(), inputs);
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
                components.Add(shader.RuntimeIdentity);
                return;
            case GeometryRenderFragmentPayload geometry:
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
