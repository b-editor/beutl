namespace Beutl.AgentToolkit.Rendering;

public sealed record RenderStoryboardShot(
    string Name,
    double TimeSeconds,
    string StillPath,
    StillFrameVisibilityAnalysis? VisibilityAnalysis,
    string Kind = "shot",
    int SubdivisionLevel = 0);
