using System.Collections.Immutable;
using Beutl.Graphics.Rendering.Cache;

namespace Beutl.Graphics.Rendering;

internal sealed class RecordedRenderGraph
{
    public RecordedRenderGraph(
        RenderRequestId requestId,
        ImmutableArray<RecordedRenderFragment> fragments,
        ImmutableArray<RecordedRenderValue> values,
        ImmutableArray<RenderFragmentId> publicationRoots,
        ImmutableArray<RootProvenance> provenance,
        ImmutableArray<RenderCacheCandidate> cacheCandidates,
        ImmutableArray<RenderResourceSlot> resources,
        ImmutableArray<RecordedNestedRenderRequest> nestedRequests)
    {
        RequestId = requestId;
        Fragments = fragments;
        Values = values;
        PublicationRoots = publicationRoots;
        Provenance = provenance;
        CacheCandidates = cacheCandidates;
        Resources = resources;
        NestedRequests = nestedRequests;
    }

    public RenderRequestId RequestId { get; }

    public ImmutableArray<RecordedRenderFragment> Fragments { get; }

    public ImmutableArray<RecordedRenderValue> Values { get; }

    public ImmutableArray<RenderFragmentId> PublicationRoots { get; }

    public ImmutableArray<RootProvenance> Provenance { get; }

    public ImmutableArray<RenderCacheCandidate> CacheCandidates { get; }

    public ImmutableArray<RenderResourceSlot> Resources { get; }

    public ImmutableArray<RecordedNestedRenderRequest> NestedRequests { get; }
}

internal sealed class RecordedRenderGraphBuilder
{
    private readonly List<RecordedRenderFragment> _fragments = [];
    private readonly List<RecordedRenderValue> _values = [];
    private readonly List<RenderFragmentId> _publicationRoots = [];
    private readonly List<RootProvenance> _provenance = [];
    private readonly List<RenderCacheCandidate> _cacheCandidates = [];
    private readonly List<RenderResourceSlot> _resources = [];
    private readonly List<RecordedNestedRenderRequest> _nestedRequests = [];
    private bool _built;

    public RecordedRenderGraphBuilder(RenderRequestId requestId)
    {
        if (requestId.Value <= 0)
        {
            throw new ArgumentException("A graph requires an initialized request ID.", nameof(requestId));
        }

        RequestId = requestId;
    }

    public RenderRequestId RequestId { get; }

    public RenderProvenanceId AddProvenance(object origin, string role)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        RenderProvenanceId id = new(RequestId, _provenance.Count + 1L);
        _provenance.Add(new RootProvenance(id, origin, role, _provenance.Count));
        return id;
    }

    public RenderValueId AddValue(
        IEnumerable<RenderValueId> inputs,
        RenderProvenanceId provenanceId,
        object? payload = null)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(inputs);
        ValidateProvenance(provenanceId);
        ImmutableArray<RenderValueId> inputCopy = [.. inputs];
        foreach (RenderValueId input in inputCopy)
        {
            ValidateExistingValue(input);
        }

        RenderValueId id = new(RequestId, _values.Count + 1L);
        _values.Add(new RecordedRenderValue(id, inputCopy, provenanceId, payload));
        return id;
    }

    public RenderFragmentId AddFragment(
        IEnumerable<RenderValueId> values,
        RenderProvenanceId provenanceId,
        object? payload = null)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(values);
        ValidateProvenance(provenanceId);
        ImmutableArray<RenderValueId> valueCopy = [.. values];
        foreach (RenderValueId value in valueCopy)
        {
            ValidateExistingValue(value);
        }

        RenderFragmentId id = new(RequestId, _fragments.Count + 1L);
        _fragments.Add(new RecordedRenderFragment(id, _fragments.Count, valueCopy, provenanceId, payload));
        return id;
    }

    public void PublishRoot(RenderFragmentId fragmentId)
    {
        EnsureMutable();
        ValidateExistingFragment(fragmentId);
        _publicationRoots.Add(fragmentId);
    }

    public RenderCacheCandidateId AddCacheCandidate(
        RenderFragmentId fragmentId,
        object cacheKey,
        RenderNodeCache? cache = null)
    {
        EnsureMutable();
        ValidateExistingFragment(fragmentId);
        ArgumentNullException.ThrowIfNull(cacheKey);

        RenderCacheCandidateId id = new(RequestId, _cacheCandidates.Count + 1L);
        _cacheCandidates.Add(new RenderCacheCandidate(
            id,
            fragmentId,
            cacheKey,
            cache,
            _cacheCandidates.Count));
        return id;
    }

    public void AddResource(RenderResourceSlot resource)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(resource);
        if (!_resources.Contains(resource))
        {
            _resources.Add(resource);
        }
    }

    public void AddNestedRequest(RecordedNestedRenderRequest nestedRequest)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(nestedRequest);
        if (nestedRequest.Request.ParentId != RequestId)
        {
            throw new InvalidOperationException("The nested request does not belong to this graph's request.");
        }

        _nestedRequests.Add(nestedRequest);
    }

    public void Append(NodeRecordingCommit commit)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(commit);

        var available = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
        foreach (RecordedRenderFragmentEntry entry in commit.Fragments)
        {
            RenderFragmentReference reference = entry.Reference;
            if (reference.Id is not null)
            {
                throw new InvalidOperationException("A recorded fragment was already committed to a graph.");
            }

            foreach (RenderFragmentReference input in reference.Inputs)
            {
                if (input.Id is null && !available.Contains(input))
                {
                    throw new InvalidOperationException(
                        "A recorded fragment input must be committed earlier in the request graph.");
                }
            }

            available.Add(reference);
        }

        var provenance = new Dictionary<object, RenderProvenanceId>(ReferenceEqualityComparer.Instance);
        foreach (RecordedRenderFragmentEntry entry in commit.Fragments)
        {
            if (!provenance.TryGetValue(entry.Origin, out RenderProvenanceId provenanceId))
            {
                provenanceId = AddProvenance(entry.Origin, entry.Role);
                provenance.Add(entry.Origin, provenanceId);
            }

            RenderFragmentReference reference = entry.Reference;
            ImmutableArray<RenderValueId> inputValues =
                [.. reference.Inputs.SelectMany(static item => item.ValueIds)];
            if (reference.ValueCardinality.Maximum != 0 || reference.ValueCardinality.Minimum != 0)
            {
                reference.ValueIds = [AddValue(inputValues, provenanceId, reference)];
            }

            reference.Id = AddFragment(reference.ValueIds, provenanceId, reference);
        }

        foreach (RenderResource resource in commit.Resources)
        {
            AddResource(resource.Slot);
        }

        foreach (RecordedNestedRenderRequest nestedRequest in commit.NestedRequests)
        {
            AddNestedRequest(nestedRequest);
        }
    }

    public RecordedRenderGraph Build()
    {
        EnsureMutable();
        _built = true;
        return new RecordedRenderGraph(
            RequestId,
            [.. _fragments],
            [.. _values],
            [.. _publicationRoots],
            [.. _provenance],
            [.. _cacheCandidates],
            [.. _resources],
            [.. _nestedRequests]);
    }

    private void ValidateProvenance(RenderProvenanceId id)
    {
        if (id.RequestId != RequestId || id.Value <= 0 || id.Value > _provenance.Count)
        {
            throw new InvalidOperationException("The provenance ID does not belong to this request graph.");
        }
    }

    private void ValidateExistingValue(RenderValueId id)
    {
        if (id.RequestId != RequestId || id.Value <= 0 || id.Value > _values.Count)
        {
            throw new InvalidOperationException("The value ID does not identify an earlier value in this request graph.");
        }
    }

    private void ValidateExistingFragment(RenderFragmentId id)
    {
        if (id.RequestId != RequestId || id.Value <= 0 || id.Value > _fragments.Count)
        {
            throw new InvalidOperationException("The fragment ID does not belong to this request graph.");
        }
    }

    private void EnsureMutable()
    {
        if (_built)
        {
            throw new InvalidOperationException("A recorded render graph builder cannot change after Build.");
        }
    }
}

internal sealed record RecordedRenderFragment(
    RenderFragmentId Id,
    int AuthoredOrder,
    ImmutableArray<RenderValueId> Values,
    RenderProvenanceId ProvenanceId,
    object? Payload);

internal sealed record RecordedRenderValue(
    RenderValueId Id,
    ImmutableArray<RenderValueId> Inputs,
    RenderProvenanceId ProvenanceId,
    object? Payload);

internal sealed record RootProvenance(
    RenderProvenanceId Id,
    object Origin,
    string Role,
    int AuthoredOrder);

internal sealed record RenderCacheCandidate(
    RenderCacheCandidateId Id,
    RenderFragmentId FragmentId,
    object CacheKey,
    RenderNodeCache? Cache,
    int AuthoredOrder);

internal readonly record struct RenderFragmentId(RenderRequestId RequestId, long Value);

internal readonly record struct RenderValueId(RenderRequestId RequestId, long Value);

internal readonly record struct RenderProvenanceId(RenderRequestId RequestId, long Value);

internal readonly record struct RenderCacheCandidateId(RenderRequestId RequestId, long Value);
