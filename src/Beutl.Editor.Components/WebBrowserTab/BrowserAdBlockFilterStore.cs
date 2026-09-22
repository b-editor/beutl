using System.Net;
using System.Text;
using System.Text.Json;

namespace Beutl.Editor.Components.WebBrowserTab;

internal sealed class BrowserAdBlockFilterStore
{
    internal const string EasyListUrl = "https://easylist.to/easylist/easylist.txt";
    internal const int MaximumDownloadBytes = 16 * 1024 * 1024;
    private static readonly HttpClient Client = new(new HttpClientHandler
    {
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.All,
        AllowAutoRedirect = false
    })
    { Timeout = TimeSpan.FromSeconds(45) };
    private readonly string _cachePath;
    private readonly HttpClient _client;
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private Task<BrowserAdBlockRules>? _load;
    internal BrowserAdBlockRules? Current { get; private set; }
    internal DateTimeOffset? UpdatedAt { get; private set; }
    internal event Action? Changed;

    internal BrowserAdBlockFilterStore(string cachePath, HttpClient? client = null)
    {
        _cachePath = cachePath;
        _client = client ?? Client;
    }

    internal static string[] ParseUrls(string text)
    {
        string[] urls = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (urls.Length is 0 or > 8) throw new InvalidDataException(Strings.BrowserAdBlockInvalidUrls);
        foreach (string url in urls)
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps
                || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
                throw new InvalidDataException(Strings.BrowserAdBlockInvalidUrls);
        return urls;
    }

    internal Task<BrowserAdBlockRules> GetAsync(string[] urls)
    {
        if (_load?.IsFaulted == true || _load?.IsCanceled == true) _load = null;
        return _load ??= LoadAsync(urls);
    }

    private async Task<BrowserAdBlockRules> LoadAsync(string[] urls)
    {
        if (Current != null) return Current;
        try
        {
            if (File.Exists(_cachePath) && new FileInfo(_cachePath).Length <= MaximumDownloadBytes * 2L)
            {
                Cache? cache = JsonSerializer.Deserialize<Cache>(await File.ReadAllTextAsync(_cachePath));
                if (cache is { Version: 1, Text: { Length: > 0 and <= MaximumDownloadBytes } })
                {
                    BrowserAdBlockRules rules = await Task.Run(() => BrowserAdBlockRules.Parse(cache.Text));
                    if (rules.SupportedCount > 0)
                    {
                        Current = rules;
                        UpdatedAt = cache.UpdatedAt;
                        Changed?.Invoke();
                        return rules;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return await UpdateAsync(urls, CancellationToken.None);
    }

    internal async Task<BrowserAdBlockRules> UpdateAsync(string[] urls, CancellationToken cancellationToken)
    {
        ParseUrls(string.Join('\n', urls));
        await _updateLock.WaitAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        cancellationToken = timeout.Token;
        string temporary = _cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var text = new StringBuilder();
            int bytesRead = 0;
            foreach (string url in urls)
            {
                Uri uri = new(url);
                using HttpResponseMessage response = await GetResponseAsync(uri, cancellationToken);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaximumDownloadBytes - bytesRead)
                    throw new InvalidDataException(Strings.BrowserAdBlockListTooLarge);
                await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var buffer = new MemoryStream();
                byte[] chunk = new byte[16384];
                int read;
                while ((read = await stream.ReadAsync(chunk, cancellationToken)) != 0)
                {
                    bytesRead += read;
                    if (bytesRead > MaximumDownloadBytes) throw new InvalidDataException(Strings.BrowserAdBlockListTooLarge);
                    buffer.Write(chunk, 0, read);
                }
                string list = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length).TrimStart('\uFEFF');
                // Reject login/error pages instead of treating their text as blocking patterns.
                if (!list.StartsWith("[Adblock", StringComparison.OrdinalIgnoreCase) && !list.StartsWith('!'))
                    throw new InvalidDataException(Strings.BrowserAdBlockInvalidList);
                text.AppendLine(list);
            }
            string combined = text.ToString();
            BrowserAdBlockRules rules = await Task.Run(() => BrowserAdBlockRules.Parse(combined), cancellationToken);
            if (rules.SupportedCount == 0) throw new InvalidDataException(Strings.BrowserAdBlockInvalidList);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new Cache(1, urls, now, combined)), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, _cachePath, overwrite: true);
            Current = rules;
            UpdatedAt = now;
            _load = Task.FromResult(rules);
            Changed?.Invoke();
            return rules;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            finally { _updateLock.Release(); }
        }
    }

    private async Task<HttpResponseMessage> GetResponseAsync(Uri uri, CancellationToken token)
    {
        for (int redirects = 0; ; redirects++)
        {
            var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect
                or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)) return response;
            Uri? location = response.Headers.Location;
            response.Dispose();
            if (redirects >= 4 || location == null) throw new HttpRequestException("Too many filter list redirects.");
            uri = new Uri(uri, location);
            ParseUrls(uri.AbsoluteUri);
        }
    }

    private sealed record Cache(int Version, string[] Urls, DateTimeOffset UpdatedAt, string Text);
}
