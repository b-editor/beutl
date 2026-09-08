namespace Beutl.Graphics.Rendering.Requests;

internal readonly record struct RenderCacheDecision(
    RenderCacheCandidate Candidate,
    RenderCacheResolutionKind Kind,
    RenderCacheBypassReason BypassReason,
    RenderOutputCacheIdentity? MissIdentity,
    RenderCacheEntry? HitEntry,
    RenderCacheCandidateId SupersededBy = default)
{
    public RenderOutputCacheIdentity? Identity => HitEntry?.Identity ?? MissIdentity;
}
