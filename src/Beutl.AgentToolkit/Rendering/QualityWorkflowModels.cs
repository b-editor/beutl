namespace Beutl.AgentToolkit.Rendering;

public sealed record QualityFixSuggestionsResponse(
    bool PassesQualityGate,
    string Verdict,
    IReadOnlyList<QualityFixSuggestion> Suggestions,
    QualityMetrics Metrics,
    IReadOnlyList<string> ReviewNotes);

public sealed record QualityFixSuggestion(
    string Category,
    string Severity,
    int IssueCount,
    string Summary,
    string MinimalPatchStrategy,
    IReadOnlyList<string> ElementIds,
    IReadOnlyList<string> ObjectIds);

public sealed record FinalPreflightResponse(
    bool ReadyForExport,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<PreflightStillFrame> StillFrames,
    MotionVariationResponse? Motion,
    QualityReviewResponse Quality,
    string RecommendedNextTool)
{
    public bool ReadyForStoryboard { get; init; }
}

public sealed record PreflightStillFrame(
    string OutputPath,
    string Time,
    IReadOnlyList<string> Warnings,
    StillFrameVisibilityAnalysis? VisibilityAnalysis,
    IReadOnlyList<RenderStillActiveElement>? ActiveElements);
