namespace Beutl.Graphics.Rendering.Requests;

internal sealed record RenderCacheDecision(
    RenderCacheCandidate Candidate,
    RenderCacheResolutionKind Kind,
    RenderCacheBypassReason BypassReason,
    RenderOutputCacheIdentity? Identity,
    RenderCacheHitSubstitution? Hit,
    RenderCacheMissCapture? MissCapture,
    RenderCacheCandidateId? SupersededBy);
