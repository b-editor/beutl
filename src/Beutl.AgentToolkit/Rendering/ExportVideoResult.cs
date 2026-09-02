namespace Beutl.AgentToolkit.Rendering;

public sealed record ExportVideoResult(
    string Status,
    string? JobId,
    ExportVideoResponse? Result);
