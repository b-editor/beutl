namespace Beutl.Graphics.Rendering;

internal sealed record RecordedNestedRenderTarget(
    RecordedNestedRenderRequest Recording,
    RenderResource<NestedRenderTargetBinding> Binding,
    NestedRenderTargetBinding Target)
{
    public RenderRequest Request => Recording.Request;

    public RecordedRenderGraph Graph => Recording.Graph;
}
