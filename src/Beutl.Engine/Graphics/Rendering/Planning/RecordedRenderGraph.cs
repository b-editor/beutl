using System.Collections.Immutable;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed class RecordedRenderGraph
{
    public RecordedRenderGraph(
        RenderRequestId requestId,
        ImmutableArray<RecordedRenderFragment> fragments,
        ImmutableArray<RecordedRenderValue> values,
        ImmutableArray<RenderFragmentId> publicationRoots,
        ImmutableArray<RootProvenance> provenance,
        ImmutableArray<RenderCacheCandidate> cacheCandidates,
        ImmutableArray<RenderResourceRegistration> resources,
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

    public ImmutableArray<RenderResourceRegistration> Resources { get; }

    public ImmutableArray<RecordedNestedRenderRequest> NestedRequests { get; }
}

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
