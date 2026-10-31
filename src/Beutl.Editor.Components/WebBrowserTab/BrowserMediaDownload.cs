using System.Net;
using System.Net.Http;
using System.Text;

namespace Beutl.Editor.Components.WebBrowserTab;

internal enum BrowserReferrerPolicy { Origin, NoReferrer, SameOrigin }

internal sealed class BrowserMediaDownload(HttpClient client)
{
    // The leading bytes checked for an HTML document before anything is written to disk.
    private const int SniffLength = 4096;
    private const int CopyBufferSize = 81920;

    // Redirects and cookies belong to each download, not to this shared transport.
    internal static readonly BrowserMediaDownload Default = new(new HttpClient(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
    })
    { Timeout = TimeSpan.FromSeconds(30) });

    private static readonly Dictionary<string, string> s_mediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["video/mp4"] = ".mp4",
        ["video/webm"] = ".webm",
        ["video/quicktime"] = ".mov",
        ["video/x-matroska"] = ".mkv",
        ["video/x-msvideo"] = ".avi",
        ["video/mpeg"] = ".mpeg",
        ["audio/mpeg"] = ".mp3",
        ["audio/mp4"] = ".m4a",
        ["audio/wav"] = ".wav",
        ["audio/x-wav"] = ".wav",
        ["audio/flac"] = ".flac",
        ["audio/ogg"] = ".ogg",
        ["audio/opus"] = ".opus",
        ["audio/aac"] = ".aac",
        ["audio/webm"] = ".webm",
        ["application/ogg"] = ".ogg",
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/gif"] = ".gif",
        ["image/webp"] = ".webp",
        ["image/bmp"] = ".bmp",
        ["image/svg+xml"] = ".svg"
    };

    internal static bool IsMediaLink(Uri uri) => IsHttpUri(uri)
        && IsMediaExtension(Path.GetExtension(uri.AbsolutePath));

    internal static bool IsMediaFile(string path) => IsMediaExtension(Path.GetExtension(path));

    internal static bool IsMediaType(string? mediaType) => mediaType != null && s_mediaTypes.ContainsKey(mediaType);

    // WebKit shows pages, PDFs and text itself. Media, attachments and types it cannot show are downloads.
    internal static bool IsDownloadResponse(string? mediaType, bool canShowMediaType, string? contentDisposition) =>
        IsMediaType(mediaType) || !canShowMediaType
        || contentDisposition?.Split(';', 2)[0].Trim().Equals("attachment", StringComparison.OrdinalIgnoreCase) == true;

    // HttpStatusCode.Redirect and HttpStatusCode.Found are both 302.
    internal static bool IsRedirectStatus(HttpStatusCode status) => status is HttpStatusCode.MovedPermanently
        or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    internal static bool IsHttpUri(Uri uri) => uri.IsAbsoluteUri
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && string.IsNullOrEmpty(uri.UserInfo);

    // A page script generated the file and handed it to the engine as a blob: or data: URL. No server holds it, so
    // only the engine's own transfer can save it, and it can never be requested again.
    internal static bool IsGeneratedFileAddress(string? address) =>
        address != null && (address.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)
            || address.StartsWith("data:", StringComparison.OrdinalIgnoreCase));

    // A blob: URL carries the origin of the document that created it; a data: URL names no one.
    internal static Uri? GetGeneratedFileOrigin(string? address) =>
        address != null && address.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)
        && Uri.TryCreate(address[5..], UriKind.Absolute, out Uri? creator) && IsHttpUri(creator)
            ? ToOrigin(creator) : null;

    // Uri names a scheme's default port when an address leaves it out, so both spellings compare equal.
    internal static bool IsSameOrigin(Uri a, Uri b) => a.Scheme == b.Scheme && a.IdnHost == b.IdnHost && a.Port == b.Port;

    private static Uri ToOrigin(Uri uri) => new(uri.GetLeftPart(UriPartial.Authority) + "/");

    private static bool IsMediaExtension(string extension) =>
        s_mediaTypes.Values.Contains(extension, StringComparer.OrdinalIgnoreCase)
        || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);

    internal static Uri? NormalizeReferrer(Uri? referrer, Uri destination, BrowserReferrerPolicy policy = BrowserReferrerPolicy.Origin)
    {
        if (policy is not (BrowserReferrerPolicy.Origin or BrowserReferrerPolicy.SameOrigin)) return null;
        if (referrer == null || !IsHttpUri(referrer) || !IsHttpUri(destination)
            || (referrer.Scheme == Uri.UriSchemeHttps && destination.Scheme == Uri.UriSchemeHttp)) return null;
        if (policy == BrowserReferrerPolicy.SameOrigin && !IsSameOrigin(referrer, destination)) return null;

        // Send only the initiating site's origin, never its path or query, including across CDN redirects.
        return ToOrigin(referrer);
    }

    // The header text of an origin from NormalizeReferrer. Headers are ASCII, and Uri.AbsoluteUri keeps an
    // internationalized host in Unicode, so the host is named in Punycode, as browsers send it.
    internal static string FormatReferrerHeader(Uri origin)
    {
        string host = origin.HostNameType == UriHostNameType.IPv6 ? $"[{origin.IdnHost}]" : origin.IdnHost;
        return origin.IsDefaultPort ? $"{origin.Scheme}://{host}/" : $"{origin.Scheme}://{host}:{origin.Port}/";
    }

    internal async Task<string> DownloadAsync(Uri uri, string directory, string? suggestedName,
        IProgress<(long Received, long? Total)>? progress, CancellationToken cancellationToken, Uri? referrer = null,
        IReadOnlyList<Cookie>? cookies = null, BrowserReferrerPolicy referrerPolicy = BrowserReferrerPolicy.Origin,
        string? userAgent = null)
    {
        if (!IsHttpUri(uri))
        {
            throw new InvalidOperationException(Strings.WebDownloadUnsupported);
        }

        using HttpResponseMessage response = await SendAsync(uri, referrer, cookies ?? [], cancellationToken, referrerPolicy, userAgent);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode == HttpStatusCode.PartialContent)
            throw new IOException(Strings.WebDownloadIncomplete);
        // The handler decodes the outermost supported encoding. Never publish a representation
        // that still has an unsupported or additional compression layer.
        if (response.Content.Headers.ContentEncoding.Any(encoding => !encoding.Equals("identity", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(Strings.WebDownloadUnsupported);
        Uri finalUri = response.RequestMessage?.RequestUri ?? uri;
        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        string? nameHint = NormalizeFileName(response.Content.Headers.ContentDisposition?.FileNameStar)
            ?? NormalizeFileName(response.Content.Headers.ContentDisposition?.FileName)
            ?? NormalizeFileName(suggestedName);
        string name = CreateFileName(nameHint, finalUri, mediaType);
        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await SaveAsync(input, directory, name, progress, cancellationToken,
            response.Content.Headers.ContentLength, response.Content.Headers.ContentType?.CharSet);
    }

    internal static async Task<string> SaveAsync(Stream input, string directory, string name,
        IProgress<(long Received, long? Total)>? progress, CancellationToken cancellationToken,
        long? total = null, string? charset = null)
    {
        byte[] prefix = await ReadPrefixAsync(input, cancellationToken);
        var inspector = new BrowserMediaContentInspector(prefix, isFinal: prefix.Length < SniffLength,
            charset: charset);
        ThrowIfHtml(inspector);
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $".{Guid.NewGuid():N}.part");
        try
        {
            long received = 0;
            var progressTimer = Stopwatch.StartNew();
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             CopyBufferSize, FileOptions.Asynchronous))
            {
                if (prefix.Length > 0)
                {
                    await output.WriteAsync(prefix, cancellationToken);
                    received = prefix.Length;
                    progress?.Report((received, total));
                }
                byte[] buffer = new byte[CopyBufferSize];
                int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    inspector.Inspect(buffer.AsSpan(0, count));
                    ThrowIfHtml(inspector);
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    received += count;
                    if (progressTimer.ElapsedMilliseconds >= 100 || received == total)
                    {
                        progress?.Report((received, total));
                        progressTimer.Restart();
                    }
                }
                inspector.Inspect([], isFinal: true);
                ThrowIfHtml(inspector);
            }

            if (received == 0 || (total.HasValue && total != received))
            {
                throw new IOException(Strings.WebDownloadIncomplete);
            }

            return PublishCompletedFile(temporaryPath, directory, name, cancellationToken);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    internal static async Task<string> ValidateAndPublishAsync(string completedPath, string directory, string name,
        string? charset, CancellationToken cancellationToken)
    {
        await using (var input = new FileStream(completedPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                         CopyBufferSize, FileOptions.Asynchronous))
        {
            long expectedLength = input.Length;
            byte[] prefix = await ReadPrefixAsync(input, cancellationToken);
            var inspector = new BrowserMediaContentInspector(prefix, isFinal: prefix.Length < SniffLength, charset: charset);
            ThrowIfHtml(inspector);
            long received = prefix.Length;
            byte[] buffer = new byte[CopyBufferSize];
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                inspector.Inspect(buffer.AsSpan(0, read));
                ThrowIfHtml(inspector);
                received += read;
            }
            inspector.Inspect([], isFinal: true);
            ThrowIfHtml(inspector);
            if (received == 0 || received != expectedLength) throw new IOException(Strings.WebDownloadIncomplete);
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(directory);
        return PublishCompletedFile(completedPath, directory, name, cancellationToken);
    }

    // The HTML check keeps a server's sign-in page from passing for its file. A generated file is what its page
    // made, HTML or empty included, so it is published as it is.
    internal static string PublishGeneratedFile(string completedPath, string directory, string name,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(directory);
        return PublishCompletedFile(completedPath, directory, name, cancellationToken);
    }

    // A browser engine saves its own transfer into a fresh folder in the destination; its handler removes the folder
    // afterwards.
    internal static (string Directory, string File) CreateStagingPaths(string directory)
    {
        string staging = Path.Combine(Path.GetFullPath(directory), $".beutl-download-{Guid.NewGuid():N}");
        return (staging, Path.Combine(staging, "response"));
    }

    internal static async Task<string> PublishNativeFileAsync(string staged, string directory, string name, bool generated,
        string? charset, CancellationToken cancellationToken) =>
        generated
            ? PublishGeneratedFile(staged, directory, name, cancellationToken)
            : await ValidateAndPublishAsync(staged, directory, name, charset, cancellationToken);

    // Fills the prefix unless the stream ends first, in which case it is trimmed to the bytes that arrived.
    private static async Task<byte[]> ReadPrefixAsync(Stream input, CancellationToken cancellationToken)
    {
        byte[] prefix = new byte[SniffLength];
        int length = 0;
        while (length < prefix.Length)
        {
            int count = await input.ReadAsync(prefix.AsMemory(length), cancellationToken);
            if (count == 0) break;
            length += count;
        }
        Array.Resize(ref prefix, length);
        return prefix;
    }

    private static void ThrowIfHtml(BrowserMediaContentInspector inspector)
    {
        if (inspector.IsHtml) throw new InvalidOperationException(Strings.WebDownloadHtmlResponse);
    }

    private static string PublishCompletedFile(string path, string directory, string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        for (int suffix = 0; ; suffix++)
        {
            string candidate = suffix == 0 ? name
                : $"{Path.GetFileNameWithoutExtension(name)} ({suffix}){Path.GetExtension(name)}";
            string destination = Path.Combine(directory, candidate);
            try
            {
                File.Move(path, destination, overwrite: false);
                return destination;
            }
            catch (IOException) when (File.Exists(destination) || Directory.Exists(destination))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, Uri? referrer, IReadOnlyList<Cookie> cookies,
        CancellationToken cancellationToken, BrowserReferrerPolicy referrerPolicy, string? userAgent)
    {
        // The native API omits SameSite/partition metadata. Do not import another site's
        // existing login session merely because the download redirects to that site.
        CookieContainer cookieContainer = BrowserSessionCookies.CreateContainer(cookies, referrer ?? uri);
        for (int redirects = 0; ; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Referrer = NormalizeReferrer(referrer, uri, referrerPolicy);
            if (!string.IsNullOrEmpty(userAgent)) request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            if (uri.Scheme == Uri.UriSchemeHttps)
            {
                string header = cookieContainer.GetCookieHeader(uri);
                if (header.Length > 0) request.Headers.Add("Cookie", header);
            }
            HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            try
            {
                if (response.Headers.TryGetValues("Set-Cookie", out var values))
                {
                    foreach (string value in values)
                    {
                        try { cookieContainer.SetCookies(uri, value); }
                        catch (CookieException) { /* Ignore invalid response cookies, as browsers do. */ }
                    }
                }
                if (!IsRedirectStatus(response.StatusCode)
                    || response.Headers.Location is not { } location || redirects >= 10)
                    return response;

                Uri next = new(uri, location);
                if (!IsHttpUri(next) || (uri.Scheme == Uri.UriSchemeHttps && next.Scheme == Uri.UriSchemeHttp))
                    throw new InvalidOperationException(Strings.WebDownloadUnsupported);
                uri = next;
            }
            catch
            {
                response.Dispose();
                throw;
            }
            response.Dispose();
        }
    }

    internal static string? GetResponseFileName(Uri uri, string? suggestedName, string? mediaType)
    {
        if (!IsHttpUri(uri)) return null;
        try
        {
            return CreateFileName(suggestedName, uri, mediaType);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    internal static string CreateFileName(string? suggestedName, Uri uri, string? mediaType)
    {
        // A page served in place of the file is a sign-in or landing page, not the download.
        if (string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Strings.WebDownloadHtmlResponse);

        return NameFile(NormalizeFileName(suggestedName)
            ?? NormalizeFileName(Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath)))
            ?? "download", mediaType);
    }

    // A generated file can be an HTML document, and the path of its blob: or data: URL is no file name.
    internal static string CreateGeneratedFileName(string? suggestedName, string? mediaType) =>
        NameFile(NormalizeFileName(suggestedName) ?? "download", mediaType);

    private static string NameFile(string name, string? mediaType)
    {
        // Media takes the extension of its type so that it imports as what it is; other files keep their names.
        if (mediaType != null && s_mediaTypes.TryGetValue(mediaType, out string? extension))
        {
            string currentExtension = Path.GetExtension(name);
            if (!currentExtension.Equals(extension, StringComparison.OrdinalIgnoreCase)
                && !(extension == ".jpg" && currentExtension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
                && !(extension == ".ogg" && currentExtension.Equals(".opus", StringComparison.OrdinalIgnoreCase)))
            {
                name = Path.ChangeExtension(name, extension);
            }
        }

        return LimitFileName(name);
    }

    private static string LimitFileName(string name)
    {
        // Leave room within a 255-byte component for collision suffixes and a device-name prefix.
        const int maximumBytes = 240;
        if (Encoding.UTF8.GetByteCount(name) <= maximumBytes) return name;
        string extension = Path.GetExtension(name);
        string stem = Path.GetFileNameWithoutExtension(name);
        int remaining = maximumBytes - Encoding.UTF8.GetByteCount(extension);
        int characters = 0;
        foreach (Rune rune in stem.EnumerateRunes())
        {
            if (rune.Utf8SequenceLength > remaining) break;
            remaining -= rune.Utf8SequenceLength;
            characters += rune.Utf16SequenceLength;
        }
        string shortened = stem[..characters].TrimEnd(' ', '.') + extension;
        return NormalizeFileName(shortened) ?? "download" + extension;
    }

    private static string? NormalizeFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string name = Path.GetFileName(value.Trim().Trim('"').Replace('\\', '/'));
        name = string.Concat(name.Select(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c) ? '_' : c)).Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(name)) return null;

        // Windows reserves these basenames even before multiple extensions. Keep names portable.
        int dot = name.IndexOf('.');
        string stem = (dot < 0 ? name : name[..dot]).TrimEnd(' ').ToUpperInvariant();
        bool reserved = stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || (stem.Length == 4 && (stem[..3] is "COM" or "LPT")
                && (stem[3] is >= '1' and <= '9' or '¹' or '²' or '³'));
        return reserved ? "_" + name : name;
    }
}
