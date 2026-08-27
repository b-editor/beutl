using Beutl.Language;

namespace Beutl.Services.AI;

internal static class AiErrorMessage
{
    public static string? Localize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim() switch
            {
                "aiProviderError" => Strings.AiProviderError,
                { } error => error,
            };
}
