namespace Beutl.Graphics.Rendering.Requests;

internal sealed record RecordedRenderFragmentEntry(
    RenderFragmentReference Reference,
    object Origin,
    string Role);
