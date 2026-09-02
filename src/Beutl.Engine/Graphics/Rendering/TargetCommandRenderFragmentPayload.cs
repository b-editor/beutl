namespace Beutl.Graphics.Rendering;

internal sealed record TargetCommandRenderFragmentPayload(
    TargetCommandDescription Description,
    IReadOnlyList<RenderInputReadback> InputReadbacks);
