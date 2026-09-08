namespace Beutl.AgentToolkit.Rendering;

public sealed record RenderStoryboardResult(
    string Status,
    string? JobId,
    RenderStoryboardResponse? Result);
