using System.Diagnostics.CodeAnalysis;

namespace Beutl.Api.Services;

internal static class AiReplayInputValidation
{
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
