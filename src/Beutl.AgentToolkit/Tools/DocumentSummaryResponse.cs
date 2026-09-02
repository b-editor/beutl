namespace Beutl.AgentToolkit.Tools;

public sealed record DocumentSummaryResponse(
    string Session,
    string Source,
    string RootId,
    string Name,
    int Width,
    int Height,
    string Duration,
    int ElementCount,
    IReadOnlyList<ElementSummary> Elements);
