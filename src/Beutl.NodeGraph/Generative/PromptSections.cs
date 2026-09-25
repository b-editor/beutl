namespace Beutl.NodeGraph.Generative;

/// <summary>
/// How a structured prompt is written out for the AI API. The AI dialogs and the prompt
/// node share it, so the same fields reach the server as the same text wherever they
/// were typed.
/// </summary>
public static class PromptSections
{
    public static string Compose(
        string? main,
        string? style = null,
        string? composition = null,
        string? motion = null,
        string? exclusions = null)
    {
        var sections = new List<string>(5);
        AddSection(sections, null, main);
        AddSection(sections, "Style", style);
        AddSection(sections, "Composition", composition);
        AddSection(sections, "Motion", motion);
        AddSection(sections, "Avoid", exclusions);
        return string.Join("\n", sections);
    }

    private static void AddSection(List<string> sections, string? label, string? value)
    {
        string? normalized = Normalize(value);
        if (normalized is null)
            return;

        sections.Add(label is null ? normalized : $"{label}: {normalized}");
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return string.Join(
            " ",
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
