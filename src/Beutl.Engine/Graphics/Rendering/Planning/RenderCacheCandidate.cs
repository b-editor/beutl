using Beutl.Graphics.Rendering.Cache;

namespace Beutl.Graphics.Rendering;

internal sealed record RenderCacheCandidate(
    RenderCacheCandidateId Id,
    RenderFragmentId FragmentId,
    object CacheKey,
    RenderNodeCache? Cache,
    int AuthoredOrder);
