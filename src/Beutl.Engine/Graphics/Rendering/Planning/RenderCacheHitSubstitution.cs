using System.Collections.Immutable;

namespace Beutl.Graphics.Rendering;

internal sealed record RenderCacheHitSubstitution(
    RenderCacheCandidateId CandidateId,
    RenderFragmentId OriginalProducerId,
    ImmutableArray<RenderValueId> OriginalValueIds,
    RenderProvenanceId ProvenanceId,
    RenderOutputCacheIdentity Identity,
    RenderCacheEntry Entry);
