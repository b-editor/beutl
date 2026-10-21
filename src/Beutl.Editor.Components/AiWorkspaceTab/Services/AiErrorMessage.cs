using Beutl.Language;

namespace Beutl.Editor.Components.AiWorkspaceTab.Services;

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
