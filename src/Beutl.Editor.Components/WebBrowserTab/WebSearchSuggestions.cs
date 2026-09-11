using System.Net.Http;
using System.Text.Json;

namespace Beutl.Editor.Components.WebBrowserTab;

internal sealed class WebSearchSuggestions(HttpClient client)
{
    internal static readonly WebSearchSuggestions Default = new(new HttpClient { Timeout = TimeSpan.FromSeconds(3) });

    internal static bool IsSearchQuery(string? text)
    {
        string query = text?.Trim() ?? string.Empty;
        if (query.Length == 0 || query.Contains("://", StringComparison.Ordinal)
            || query.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (query.StartsWith('[')
            && Uri.TryCreate($"https://{query}", UriKind.Absolute, out Uri? address)
            && address.HostNameType == UriHostNameType.IPv6)
        {
            return false;
        }

        int colon = query.IndexOf(':');
        if (colon > 0 && Uri.CheckSchemeName(query[..colon]))
        {
            return false;
        }

        return query.Contains('@') || query.Any(char.IsWhiteSpace)
            || (!query.Contains('/') && !query.Contains('\\') && !query.Contains('?') && !query.Contains('.'));
    }

    internal static Uri CreateSearchUri(string query, BrowserSearchEngine engine = BrowserSearchEngine.Google) =>
        new((engine == BrowserSearchEngine.Bing ? "https://www.bing.com/search?q=" : "https://www.google.com/search?q=")
            + Uri.EscapeDataString(query.Trim()));

    internal async Task<IReadOnlyList<string>> GetSuggestionsAsync(string query, CancellationToken cancellationToken,
        BrowserSearchEngine engine = BrowserSearchEngine.Google)
    {
        if (query.Trim().Length is < 2 or > 200 || query.Contains('@') || !IsSearchQuery(query))
        {
            return [];
        }

        string json = await client.GetStringAsync(
            (engine == BrowserSearchEngine.Bing ? "https://api.bing.com/osjson.aspx?query="
                : "https://suggestqueries.google.com/complete/search?client=firefox&q=") + Uri.EscapeDataString(query.Trim()),
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
