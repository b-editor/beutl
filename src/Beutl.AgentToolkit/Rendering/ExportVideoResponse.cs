namespace Beutl.AgentToolkit.Rendering;

public sealed record ExportVideoResponse(
    string OutputPath,
    long Frames,
    long Samples,
    string Duration,
    string Encoder,
    IReadOnlyList<string> Warnings);
