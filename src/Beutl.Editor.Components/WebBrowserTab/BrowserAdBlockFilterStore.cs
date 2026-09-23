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
    private readonly Func<string, CancellationToken, Task<BrowserAdBlockRules>> _parseRules;
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private readonly object _stateLock = new();
    private LoadRequest? _request;
    private PublishedFilters? _current;
    internal BrowserAdBlockRules? Current { get { lock (_stateLock) return _current?.Rules; } }
    internal DateTimeOffset? UpdatedAt { get { lock (_stateLock) return _current?.UpdatedAt; } }
    internal event Action? Changed;

    internal BrowserAdBlockFilterStore(string cachePath, HttpClient? client = null,
        Func<string, CancellationToken, Task<BrowserAdBlockRules>>? parseRules = null)
    {
        _cachePath = cachePath;
        _client = client ?? Client;
        _parseRules = parseRules ?? ((text, token) => Task.Run(() => BrowserAdBlockRules.Parse(text), token));
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

    internal Task<BrowserAdBlockRules> GetAsync(string[] urls) => StartRequest(urls, false, CancellationToken.None);

    internal Task<BrowserAdBlockRules> UpdateAsync(string[] urls, CancellationToken cancellationToken) =>
        StartRequest(urls, true, cancellationToken);

    internal bool IsSuperseded(Task<BrowserAdBlockRules> task)
    {
        lock (_stateLock) return !ReferenceEquals(_request?.Completion.Task, task);
    }

    private Task<BrowserAdBlockRules> StartRequest(string[] urls, bool update, CancellationToken cancellationToken)
    {
        urls = ParseUrls(string.Join('\n', urls));
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<BrowserAdBlockRules>(cancellationToken);
        LoadRequest request;
        lock (_stateLock)
        {
            if (!update && _request is { } pending && SourcesMatch(pending.Urls, urls)
                && !pending.Completion.Task.IsFaulted && !pending.Completion.Task.IsCanceled)
                return pending.Completion.Task;

            // Each request object is a distinct generation, including forced updates to
            // the same URLs. Install its URL/task pair before starting any asynchronous work.
            request = new LoadRequest(urls);
            _request = request;
            if (!update && _current is { } current && SourcesMatch(current.Urls, urls))
            {
                request.Completion.SetResult(current.Rules);
                return request.Completion.Task;
            }
        }
        _ = RunRequestAsync(request, update, cancellationToken);
        return request.Completion.Task;
    }

    private static bool SourcesMatch(string[]? cached, string[] requested) =>
        cached != null && cached.SequenceEqual(requested, StringComparer.Ordinal);

    private async Task RunRequestAsync(LoadRequest request, bool update, CancellationToken cancellationToken)
    {
        try
        {
            EnsureCurrent(request, cancellationToken);
            PublishedFilters? filters = update ? null : await ReadCacheAsync(request, cancellationToken).ConfigureAwait(false);
            if (filters != null) Publish(request, filters, null, cancellationToken);
            else filters = await DownloadAsync(request, cancellationToken).ConfigureAwait(false);

            EnsureCurrent(request, CancellationToken.None);
            // Subscribers may re-enter the store; never invoke them while holding its lock.
            Changed?.Invoke();
            lock (_stateLock)
            {
                EnsureCurrent(request, CancellationToken.None);
                request.Completion.TrySetResult(filters.Rules);
            }
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                if (!ReferenceEquals(_request, request)) request.Completion.TrySetCanceled();
                else if (ex is OperationCanceledException canceled) request.Completion.TrySetCanceled(canceled.CancellationToken);
                else request.Completion.TrySetException(ex);
            }
        }
    }

    private async Task<PublishedFilters?> ReadCacheAsync(LoadRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (File.Exists(_cachePath) && new FileInfo(_cachePath).Length <= MaximumDownloadBytes * 2L)
            {
                Cache? cache = JsonSerializer.Deserialize<Cache>(await File.ReadAllTextAsync(_cachePath, cancellationToken).ConfigureAwait(false));
                if (cache is { Version: 1, Text: { Length: > 0 and <= MaximumDownloadBytes } }
                    && SourcesMatch(cache.Urls, request.Urls))
                {
                    EnsureCurrent(request, cancellationToken);
                    BrowserAdBlockRules rules = await _parseRules(cache.Text, cancellationToken).ConfigureAwait(false);
                    if (rules.SupportedCount > 0)
                        return new PublishedFilters(request.Urls, rules, cache.UpdatedAt);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return null;
    }

    private async Task<PublishedFilters> DownloadAsync(LoadRequest request, CancellationToken cancellationToken)
    {
        // A stale cache miss/failure must not start a new public UpdateAsync generation.
        EnsureCurrent(request, cancellationToken);
        await _updateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        string temporary = _cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            EnsureCurrent(request, cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            cancellationToken = timeout.Token;
            var text = new StringBuilder();
            int bytesRead = 0;
            foreach (string url in request.Urls)
            {
                EnsureCurrent(request, cancellationToken);
                Uri uri = new(url);
                using HttpResponseMessage response = await GetResponseAsync(uri, cancellationToken).ConfigureAwait(false);
                EnsureCurrent(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaximumDownloadBytes - bytesRead)
                    throw new InvalidDataException(Strings.BrowserAdBlockListTooLarge);
                await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var buffer = new MemoryStream();
                byte[] chunk = new byte[16384];
                int read;
                while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) != 0)
                {
                    EnsureCurrent(request, cancellationToken);
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
            EnsureCurrent(request, cancellationToken);
            BrowserAdBlockRules rules = await _parseRules(combined, cancellationToken).ConfigureAwait(false);
            if (rules.SupportedCount == 0) throw new InvalidDataException(Strings.BrowserAdBlockInvalidList);
            EnsureCurrent(request, cancellationToken);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new Cache(1, request.Urls, now, combined)), cancellationToken).ConfigureAwait(false);
            var filters = new PublishedFilters(request.Urls, rules, now);
            Publish(request, filters, temporary, cancellationToken);
            return filters;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            finally { _updateLock.Release(); }
        }
    }

    private void EnsureCurrent(LoadRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateLock)
        {
            if (!ReferenceEquals(_request, request)) throw new OperationCanceledException("The filter request was superseded.");
        }
    }

    private void Publish(LoadRequest request, PublishedFilters filters, string? temporary, CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            EnsureCurrent(request, cancellationToken);
            // The generation check and disk replacement must be indivisible with respect
            // to selecting another subscription. Memory and source metadata move together.
            if (temporary != null) File.Move(temporary, _cachePath, overwrite: true);
            _current = filters;
        }
    }

    private async Task<HttpResponseMessage> GetResponseAsync(Uri uri, CancellationToken token)
    {
        for (int redirects = 0; ; redirects++)
        {
            var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
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
    private sealed record PublishedFilters(string[] Urls, BrowserAdBlockRules Rules, DateTimeOffset UpdatedAt);
    private sealed class LoadRequest(string[] urls)
    {
        internal string[] Urls { get; } = urls;
        internal TaskCompletionSource<BrowserAdBlockRules> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
