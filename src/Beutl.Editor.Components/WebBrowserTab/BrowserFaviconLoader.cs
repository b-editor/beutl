using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace Beutl.Editor.Components.WebBrowserTab;

internal sealed partial class BrowserFaviconLoader(HttpClient client)
{
    internal static readonly BrowserFaviconLoader Default = new(new HttpClient(new HttpClientHandler
    {
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.All
    }));

    internal const int MaximumBytes = 256 * 1024;
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _requests = new(6);
    private readonly Dictionary<Uri, CacheEntry> _cache = new();

    internal async Task<byte[]?> GetAsync(string? address, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!BrowserProfile.IsAllowedUrl(address)) return null;
        var origin = new Uri(new Uri(address!), "/");
        Task<byte[]?> task;
        lock (_lock)
        {
            if (_cache.TryGetValue(origin, out CacheEntry? entry)
                && DateTimeOffset.UtcNow - entry.CreatedAt < (entry.Task.IsCompletedSuccessfully && entry.Task.Result == null
                    ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(15)))
            {
                task = entry.Task;
            }
            else
            {
                if (_cache.Count >= 128) _cache.Remove(_cache.MinBy(pair => pair.Value.CreatedAt).Key);
                task = LoadAsync(origin);
                _cache[origin] = new CacheEntry(task, DateTimeOffset.UtcNow);
            }
        }

        // A disappearing tab stops waiting without canceling another tab's shared request.
        return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]?> LoadAsync(Uri origin)
    {
        await _requests.WaitAsync().ConfigureAwait(false);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            if (await ReadAsync(new Uri(origin, "favicon.ico"), true, timeout.Token).ConfigureAwait(false) is { } icon)
                return icon.Bytes;

            if (await ReadAsync(origin, false, timeout.Token).ConfigureAwait(false) is not { } page) return null;
            foreach (Uri candidate in FindIcons(Encoding.UTF8.GetString(page.Bytes), page.Address).Take(3))
            {
                if (await ReadAsync(candidate, true, timeout.Token).ConfigureAwait(false) is { } declaredIcon)
                    return declaredIcon.Bytes;
            }
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or RegexMatchTimeoutException)
        {
            return null;
        }
        finally { _requests.Release(); }
    }

    private async Task<Response?> ReadAsync(Uri address, bool image, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.UserAgent.ParseAdd("Beutl");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        if (image && (mediaType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true
            || mediaType == "image/svg+xml" || response.Content.Headers.ContentLength > MaximumBytes)) return null;
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var content = new MemoryStream();
        byte[] buffer = new byte[8192];
        while (content.Length <= MaximumBytes)
        {
            int count = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, MaximumBytes + 1 - content.Length)),
                cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            content.Write(buffer, 0, count);
        }
        if (image && content.Length > MaximumBytes) return null;
        if (content.Length > MaximumBytes) content.SetLength(MaximumBytes);
        return new Response(content.ToArray(), response.RequestMessage?.RequestUri ?? address);
    }

    internal static IEnumerable<Uri> FindIcons(string html, Uri page)
    {
        Uri baseAddress = page;
        bool hasBase = false;
        var icons = new List<Uri>();
        foreach (Match tag in HeadTags().Matches(html))
        {
            string name = tag.Groups["tag"].Value;
            if (name.Length == 0) continue;
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match attribute in Attributes().Matches(tag.Groups["attributes"].Value))
                attributes[attribute.Groups["name"].Value] = WebUtility.HtmlDecode(attribute.Groups["value"].Value);
            if (!attributes.TryGetValue("href", out string? href)
                || !Uri.TryCreate(baseAddress, href, out Uri? target) || !BrowserProfile.IsAllowedUrl(target.AbsoluteUri)) continue;
            if (name.Equals("base", StringComparison.OrdinalIgnoreCase))
            {
                if (!hasBase) baseAddress = target;
                hasBase = true;
            }
            else if (attributes.TryGetValue("rel", out string? rel)
                && rel.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Any(value => value.Equals("icon", StringComparison.OrdinalIgnoreCase)
                        || value.Equals("apple-touch-icon", StringComparison.OrdinalIgnoreCase)))
            {
                icons.Add(target);
            }
        }
        return icons.Distinct();
    }

    [GeneratedRegex("<!--.*?-->|<script\\b[^>]*>.*?</script\\s*>|<(?<tag>link|base)\\b(?<attributes>(?:[^\"'>]|\"[^\"]*\"|'[^']*')*)>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, 100)]
    private static partial Regex HeadTags();

    [GeneratedRegex("(?<name>[\\w-]+)\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)'|(?<value>[^\\s>]+))",
        RegexOptions.CultureInvariant, 100)]
    private static partial Regex Attributes();

    private sealed record CacheEntry(Task<byte[]?> Task, DateTimeOffset CreatedAt);
    private sealed record Response(byte[] Bytes, Uri Address);
}
