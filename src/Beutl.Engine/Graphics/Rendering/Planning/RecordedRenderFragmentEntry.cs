namespace Beutl.Graphics.Rendering;

internal sealed record RecordedRenderFragmentEntry(
    RenderFragmentReference Reference,
    object Origin,
    string Role);
