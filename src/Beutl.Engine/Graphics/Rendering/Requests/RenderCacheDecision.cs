namespace Beutl.Graphics.Rendering.Requests;

internal readonly record struct RenderCacheDecision(
    RenderCacheCandidate Candidate,
    RenderCacheResolutionKind Kind,
    RenderOutputCacheIdentity? MissIdentity,
    RenderCacheEntry? HitEntry)
{
    public RenderOutputCacheIdentity? Identity => HitEntry?.Identity ?? MissIdentity;
}
