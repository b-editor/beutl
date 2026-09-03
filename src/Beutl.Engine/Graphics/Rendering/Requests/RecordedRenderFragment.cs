using System.Collections.Immutable;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed record RecordedRenderFragment(
    RenderFragmentId Id,
    ImmutableArray<RenderValueId> Values,
    RenderProvenanceId ProvenanceId,
    object? Payload);
