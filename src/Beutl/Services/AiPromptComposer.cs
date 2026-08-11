namespace Beutl.Services;

internal sealed record AiPromptParts(
    string Main,
    string? Style = null,
    string? Composition = null,
    string? Motion = null,
    string? Exclusions = null);

internal static class AiPromptComposer
{
    public static string Compose(AiPromptParts parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var sections = new List<string>(5);
        AddSection(sections, null, parts.Main);
        AddSection(sections, "Style", parts.Style);
        AddSection(sections, "Composition", parts.Composition);
        AddSection(sections, "Motion", parts.Motion);
        AddSection(sections, "Avoid", parts.Exclusions);
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
