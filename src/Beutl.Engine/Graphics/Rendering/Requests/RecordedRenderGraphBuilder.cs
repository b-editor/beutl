using Beutl.Graphics.Rendering.Cache;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed class RecordedRenderGraphBuilder
{
    private readonly List<RenderFragmentReference> _fragments = [];
    private readonly List<RenderFragmentId> _publicationRoots = [];
    private readonly List<RenderCacheCandidate> _cacheCandidates = [];
    private readonly List<RecordedNestedRenderRequest> _nestedRequests = [];

    // Append validates one commit at a time and calls no user code, so a nested recording cannot re-enter it
    // on the same builder: RenderRequestRecorder gives every nested request a builder of its own.
    private readonly HashSet<RenderFragmentReference> _appendScratchAvailable =
        new(ReferenceEqualityComparer.Instance);

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

    public RenderFragmentId AddFragment(RenderFragmentReference reference)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(reference);
        ValidateUncommittedReference(reference);
        foreach (RenderFragmentReference input in reference.Inputs)
            ValidateCommittedReference(input);
        return CommitFragment(reference);
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

    public void Append(in NodeRecordingCommit commit)
    {
        EnsureMutable();

        HashSet<RenderFragmentReference> available = _appendScratchAvailable;
        available.Clear();
        try
        {
            foreach (RecordedRenderFragmentEntry entry in commit.Fragments)
            {
                RenderFragmentReference reference = entry.Reference;
                ValidateUncommittedReference(reference);
                if (available.Contains(reference))
                    throw new InvalidOperationException("A recorded fragment appears more than once in one commit.");

                foreach (RenderFragmentReference input in reference.Inputs)
                {
                    if (input.Id is null)
                    {
                        if (!available.Contains(input))
                        {
                            throw new InvalidOperationException(
                                "A recorded fragment input must be committed earlier in the request graph.");
                        }
                    }
                    else
                    {
                        ValidateCommittedReference(input);
                    }
                }

                available.Add(reference);
            }
        }
        finally
        {
            available.Clear();
        }

        foreach (RecordedRenderFragmentEntry entry in commit.Fragments)
            CommitFragment(entry.Reference);

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
            [.. _publicationRoots],
            [.. _cacheCandidates],
            [.. _nestedRequests]);
    }

    private RenderFragmentId CommitFragment(RenderFragmentReference reference)
    {
        RenderFragmentId id = new(RequestId, _fragments.Count + 1L);
        reference.AssignId(id);
        _fragments.Add(reference);
        return id;
    }

    private static void ValidateUncommittedReference(RenderFragmentReference reference)
    {
        if (reference.Id is not null)
            throw new InvalidOperationException("A recorded fragment was already committed to a graph.");
    }

    private void ValidateCommittedReference(RenderFragmentReference reference)
    {
        if (reference.Id is not { } id
            || id.RequestId != RequestId
            || id.Value <= 0
            || id.Value > _fragments.Count
            || !ReferenceEquals(_fragments[checked((int)id.Value - 1)], reference))
        {
            throw new InvalidOperationException(
                "A recorded fragment input must be committed earlier in the same request graph.");
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
