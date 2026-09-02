namespace Beutl.AgentToolkit.Rendering;

public sealed record RenderStillActiveElement(
    string Id,
    string Name,
    string Start,
    string Length,
    int ZIndex,
    int ObjectCount);
