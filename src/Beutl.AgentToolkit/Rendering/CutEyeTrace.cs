namespace Beutl.AgentToolkit.Rendering;

public sealed record CutEyeTrace(
    string LeftFrame,
    string RightFrame,
    NormalizedFocalPoint LeftFocalPoint,
    NormalizedFocalPoint RightFocalPoint,
    double DisplacementRatio,
    bool ExceedsEyeTraceBudget);
