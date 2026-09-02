using System.Collections.Immutable;

namespace Beutl.Graphics.Rendering.Requests;

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
    }

    public ImmutableArray<RenderCacheDecision> Decisions { get; }

    public ImmutableArray<RenderCacheHitSubstitution> Hits { get; }

    public ImmutableArray<RenderCacheMissCapture> MissCaptures { get; }

    public RenderCacheDecision GetDecision(RenderCacheCandidateId id)
        => Decisions.FirstOrDefault(item => item.Candidate.Id == id)
           ?? throw new KeyNotFoundException("The cache candidate is not part of this resolution.");

    public HashSet<RenderFragmentId> CollectPrunedHitProducers()
    {
        var result = new HashSet<RenderFragmentId>();
        foreach (RenderCacheHitSubstitution hit in Hits)
        {
            result.Add(hit.OriginalProducerId);
        }

        return result;
    }
}
