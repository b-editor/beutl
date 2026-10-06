using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Beutl.Api.Services;

internal static class AiReplayInputValidation
{
    // A retained prompt replays only as exactly what a request would have sent: non-blank, already trimmed and
    // within the prompt limit.
    public static bool IsCanonicalPrompt([NotNullWhen(true)] string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= AiRequestLimits.MaxPromptLength
            && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    public static bool TryReadReplayObject(
        AiJob job,
        HashSet<string> allowedProperties,
        out JsonElement input,
        [NotNullWhen(true)] out string? prompt)
    {
        prompt = null;
        if (job.InputParameters is not { ValueKind: JsonValueKind.Object } parameters)
        {
            input = default;
            return false;
        }

        input = parameters;
        foreach (JsonProperty property in parameters.EnumerateObject())
        {
            if (!allowedProperties.Contains(property.Name))
                return false;
        }
        if (!parameters.TryGetProperty("prompt", out JsonElement promptElement)
            || promptElement.ValueKind != JsonValueKind.String
            || promptElement.GetString() is not { } promptValue
            || !IsCanonicalPrompt(promptValue))
            return false;

        prompt = promptValue;
        return true;
    }

    public static bool TryReadOptionalSeed(JsonElement input, out int? seed)
    {
        seed = null;
        if (input.TryGetProperty("seed", out JsonElement seedElement))
        {
            if (seedElement.ValueKind != JsonValueKind.Number
                || !seedElement.TryGetInt32(out int seedValue)
                || seedValue < AiRequestLimits.MinSeed
                || seedValue > AiRequestLimits.MaxSeed)
                return false;
            seed = seedValue;
        }

        return true;
    }

    public static bool IsStructurallyValidAspectRatio([NotNullWhen(true)] string? value)
    {
        if (value is not { Length: > 0 and <= 256 }
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        int separator = value.IndexOf(':');
        return separator > 0
            && separator == value.LastIndexOf(':')
            && separator < value.Length - 1
            && IsPositiveDecimal(value.AsSpan(0, separator))
            && IsPositiveDecimal(value.AsSpan(separator + 1));
    }

    private static bool IsPositiveDecimal(ReadOnlySpan<char> value)
    {
        bool hasNonZeroDigit = false;
        foreach (char character in value)
        {
            if (character is < '0' or > '9')
                return false;
            hasNonZeroDigit |= character != '0';
        }

        return hasNonZeroDigit;
    }
}
