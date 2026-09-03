using System.Collections.Immutable;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed class RecordedRenderGraph
{
    public RecordedRenderGraph(
        RenderRequestId requestId,
        ImmutableArray<RenderFragmentReference> fragments,
        ImmutableArray<RenderFragmentId> publicationRoots,
        ImmutableArray<RenderCacheCandidate> cacheCandidates,
        ImmutableArray<RecordedNestedRenderRequest> nestedRequests)
    {
        RequestId = requestId;
        Fragments = fragments;
        PublicationRoots = publicationRoots;
        CacheCandidates = cacheCandidates;
        NestedRequests = nestedRequests;
    }

    public RenderRequestId RequestId { get; }

    public ImmutableArray<RenderFragmentReference> Fragments { get; }

    public ImmutableArray<RenderFragmentId> PublicationRoots { get; }

    public ImmutableArray<RenderCacheCandidate> CacheCandidates { get; }

    public ImmutableArray<RecordedNestedRenderRequest> NestedRequests { get; }

    public RenderFragmentReference GetFragment(RenderFragmentId id)
    {
        if (id.RequestId != RequestId || id.Value <= 0 || id.Value > Fragments.Length)
            throw new InvalidOperationException("The fragment ID does not belong to this request graph.");

        RenderFragmentReference fragment = Fragments[checked((int)id.Value - 1)];
        if (fragment.Id != id)
            throw new InvalidOperationException("A recorded fragment has a non-canonical graph ID.");
        return fragment;
    }
}
