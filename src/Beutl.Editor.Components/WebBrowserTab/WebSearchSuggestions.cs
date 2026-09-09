using System.Net.Http;
using System.Text.Json;

namespace Beutl.Editor.Components.WebBrowserTab;

internal sealed class WebSearchSuggestions(HttpClient client)
{
    internal static readonly WebSearchSuggestions Default = new(new HttpClient { Timeout = TimeSpan.FromSeconds(3) });

    internal static bool IsSearchQuery(string? text)
    {
        string query = text?.Trim() ?? string.Empty;
        if (query.Length == 0 || query.Length > 200 || query.Contains("://", StringComparison.Ordinal)
            || query.Contains('@') || query.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int colon = query.IndexOf(':');
        if (colon > 0 && Uri.CheckSchemeName(query[..colon]))
        {
            return false;
        }

        return query.Any(char.IsWhiteSpace)
            || (!query.Contains('/') && !query.Contains('\\') && !query.Contains('?') && !query.Contains('.'));
    }

    internal static Uri CreateSearchUri(string query) =>
        new("https://www.google.com/search?q=" + Uri.EscapeDataString(query.Trim()));

    internal async Task<IReadOnlyList<string>> GetSuggestionsAsync(string query, CancellationToken cancellationToken)
    {
        if (!IsSearchQuery(query) || query.Trim().Length < 2)
        {
            return [];
        }

        string json = await client.GetStringAsync(
            "https://suggestqueries.google.com/complete/search?client=firefox&q=" + Uri.EscapeDataString(query.Trim()),
            cancellationToken);
        return ParseResponse(json);
    }

    internal static IReadOnlyList<string> ParseResponse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 2
            || root[1].ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return root[1].EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .Where(text => !string.IsNullOrWhiteSpace(text) && text.Length <= 200)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
    }
}
