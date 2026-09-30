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

    /// <summary>
    /// Splits text written by <see cref="Compose"/> back into its fields, so composing them
    /// again gives the same text. Lines without a section label belong to the main field.
    /// </summary>
    public static (string Main, string Style, string Composition, string Motion, string Exclusions) Parse(string? text)
    {
        var main = new List<string>();
        string style = string.Empty, composition = string.Empty, motion = string.Empty, exclusions = string.Empty;
        foreach (string line in (text ?? string.Empty).Split('\n'))
        {
            if (TryStrip(line, "Style", out string? value)) style = value;
            else if (TryStrip(line, "Composition", out value)) composition = value;
            else if (TryStrip(line, "Motion", out value)) motion = value;
            else if (TryStrip(line, "Avoid", out value)) exclusions = value;
            else main.Add(line);
        }

        return (string.Join(" ", main), style, composition, motion, exclusions);
    }

    private static bool TryStrip(string line, string label, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value)
    {
        string prefix = label + ": ";
        value = line.StartsWith(prefix, StringComparison.Ordinal) ? line[prefix.Length..] : null;
        return value is not null;
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
