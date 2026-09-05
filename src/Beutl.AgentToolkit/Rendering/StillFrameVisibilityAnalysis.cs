namespace Beutl.AgentToolkit.Rendering;

public sealed record StillFrameVisibilityAnalysis(
    int TotalPixels,
    int VisiblePixels,
    double VisiblePixelRatio,
    int ForegroundPixels,
    double ForegroundPixelRatio,
    double OccupiedBoundsRatio,
    double MaxQuadrantForegroundRatio,
    int Left,
    int Top,
    int Right,
    int Bottom,
    int MinLuma,
    int MaxLuma,
    double MeanLuma,
    double LumaStandardDeviation,
    double BackgroundLuma,
    int VisibilityThreshold,
    int ForegroundDeltaThreshold,
    IReadOnlyList<string> Warnings);
