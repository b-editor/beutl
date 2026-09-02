namespace Beutl.AgentToolkit.Rendering;

public sealed record RenderStillResponse(
    string OutputPath,
    int Width,
    int Height,
    string Time,
    IReadOnlyList<string> Warnings,
    StillFrameVisibilityAnalysis? VisibilityAnalysis = null,
    IReadOnlyList<RenderStillActiveElement>? ActiveElements = null);
