namespace Beutl.Graphics.Rendering.Requests;

internal sealed record RecordedNestedRenderTarget(
    RecordedNestedRenderRequest Recording,
    RenderResource<NestedRenderTargetBinding> Binding,
    NestedRenderTargetBinding Target);
