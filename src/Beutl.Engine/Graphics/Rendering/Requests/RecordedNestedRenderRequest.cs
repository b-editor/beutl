namespace Beutl.Graphics.Rendering.Requests;

internal sealed record RecordedNestedRenderRequest(
    RenderRequest Request,
    RecordedRenderGraph Graph);
