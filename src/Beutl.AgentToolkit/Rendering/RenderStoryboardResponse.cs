namespace Beutl.AgentToolkit.Rendering;

public sealed record RenderStoryboardResponse(
    string ContactSheetPath,
    IReadOnlyList<RenderStoryboardShot> Shots,
    IReadOnlyList<CutEyeTrace> CutEyeTrace,
    IReadOnlyList<string> ReviewNotes);
