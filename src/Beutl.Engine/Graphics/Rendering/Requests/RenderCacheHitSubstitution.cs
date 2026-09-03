namespace Beutl.Graphics.Rendering.Requests;

internal sealed record RenderCacheHitSubstitution(
    RenderCacheCandidateId CandidateId,
    RenderFragmentId OriginalProducerId,
    RenderOutputCacheIdentity Identity,
    RenderCacheEntry Entry);
