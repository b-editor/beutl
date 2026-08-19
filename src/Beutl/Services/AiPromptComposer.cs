using Beutl.Api.Services;
using Beutl.Language;

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
        string result = string.Join("\n", sections);
        if (result.Length > AiRequestLimits.MaxPromptLength)
        {
            throw new ArgumentException(PromptTooLongMessage, nameof(parts));
        }
        return result;
    }

    /// <summary>
    /// The reason the parts cannot be sent, worded for the person who typed them,
    /// or null when they can. The message is built here rather than taken from the
    /// exception so a caller never shows an exception's text as an explanation.
    /// </summary>
    public static string? GetValidationError(AiPromptParts parts)
    {
        try
        {
            return string.IsNullOrWhiteSpace(Compose(parts))
                ? Strings.AiPromptRequired
                : null;
        }
        catch (ArgumentException)
        {
            return PromptTooLongMessage;
        }
    }

    internal static string PromptTooLongMessage
        => string.Format(Strings.AiPromptTooLongFormat, AiRequestLimits.MaxPromptLength);

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
