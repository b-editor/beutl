using Beutl.Api.Services;
using Beutl.Language;
using Beutl.NodeGraph.Generative;

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

        string result = PromptSections.Compose(
            parts.Main,
            parts.Style,
            parts.Composition,
            parts.Motion,
            parts.Exclusions);
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
}
