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

    private static readonly string[] s_labels = ["Style", "Composition", "Motion", "Avoid"];

    /// <summary>
    /// Splits text written by <see cref="Compose"/> back into its fields, so composing them
    /// again gives the same text. That holds only for such text: other lines without a section
    /// label are joined into the main field with spaces.
    /// </summary>
    /// <remarks>
    /// Sections are read from the end, in the order <see cref="Compose"/> writes them, each at
    /// most once. A labelled line before them — a main field that itself starts with "Style: " —
    /// stays in the main field instead of being taken for, or overwritten by, a section.
    /// </remarks>
    public static (string Main, string Style, string Composition, string Motion, string Exclusions) Parse(string? text)
    {
        string[] lines = (text ?? string.Empty).Split('\n');
        var sections = new string[s_labels.Length];
        Array.Fill(sections, string.Empty);
        int end = lines.Length;
        int limit = s_labels.Length;
        while (end > 0 && FindSection(lines[end - 1], limit) is { } found)
        {
            sections[found.Index] = found.Value;
            limit = found.Index;
            end--;
        }

        return (string.Join(" ", lines[..end]), sections[0], sections[1], sections[2], sections[3]);
    }

    // The last label before limit that the line starts with, since Compose writes them in order.
    private static (int Index, string Value)? FindSection(string line, int limit)
    {
        for (int i = limit - 1; i >= 0; i--)
        {
            if (TryStrip(line, s_labels[i], out string? value))
                return (i, value);
        }

        return null;
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
