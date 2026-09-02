using System.Collections.Immutable;

namespace Beutl.Graphics.Rendering.Requests;

/// <summary>
/// Describes a capture to insert immediately after the original producer. The executor keeps the actual payload
/// request-owned and unpublished; this descriptor becomes publishable only after complete-request success.
/// </summary>
internal sealed record RenderCacheMissCapture(
    RenderCacheCandidateId CandidateId,
    RenderFragmentId ProducerId,
    ImmutableArray<RenderValueId> ValueIds,
    RenderProvenanceId ProvenanceId,
    RenderOutputCacheIdentity Identity);
