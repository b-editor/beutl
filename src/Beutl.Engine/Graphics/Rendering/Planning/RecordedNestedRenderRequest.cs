namespace Beutl.Graphics.Rendering;

internal sealed record RecordedNestedRenderRequest(
    RenderRequest Request,
    RecordedRenderGraph Graph);
