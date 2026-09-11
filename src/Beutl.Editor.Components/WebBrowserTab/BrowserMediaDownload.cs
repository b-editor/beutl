using System.Net;
using System.Net.Http;
using System.Text;

namespace Beutl.Editor.Components.WebBrowserTab;

internal sealed class BrowserMediaDownload(HttpClient client)
{
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

    internal static bool IsHttpUri(Uri uri) => uri.IsAbsoluteUri
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && string.IsNullOrEmpty(uri.UserInfo);

    private static bool IsMediaExtension(string extension) =>
        s_mediaTypes.Values.Contains(extension, StringComparer.OrdinalIgnoreCase)
        || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".opus", StringComparison.OrdinalIgnoreCase);

    internal static Uri? NormalizeReferrer(Uri? referrer, Uri destination)
    {
        if (referrer == null || !IsHttpUri(referrer) || !IsHttpUri(destination)
            || (referrer.Scheme == Uri.UriSchemeHttps && destination.Scheme == Uri.UriSchemeHttp)) return null;

        // Send only the initiating site's origin, never its path or query, including across CDN redirects.
        return new Uri(referrer.GetLeftPart(UriPartial.Authority) + "/");
    }

    internal async Task<string> DownloadAsync(Uri uri, string directory, string? suggestedName,
        IProgress<(long Received, long? Total)>? progress, CancellationToken cancellationToken, Uri? referrer = null,
        IReadOnlyList<Cookie>? cookies = null)
    {
        if (!IsHttpUri(uri))
        {
            throw new InvalidOperationException(Strings.WebDownloadUnsupported);
        }

        using HttpResponseMessage response = await SendAsync(uri, referrer, cookies ?? [], cancellationToken);
        response.EnsureSuccessStatusCode();
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
        byte[] prefix = new byte[4096];
        int length = 0;
        while (length < prefix.Length)
        {
            int count = await input.ReadAsync(prefix.AsMemory(length), cancellationToken);
            if (count == 0) break;
            length += count;
        }
        Array.Resize(ref prefix, length);
        if (LooksLikeHtml(prefix)) throw new InvalidOperationException(Strings.WebDownloadHtmlResponse);
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $".{Guid.NewGuid():N}.part");
        try
        {
            long received = 0;
            var progressTimer = Stopwatch.StartNew();
            long? total = response.Content.Headers.ContentLength;
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             81920, FileOptions.Asynchronous))
            {
                if (prefix.Length > 0)
                {
                    await output.WriteAsync(prefix, cancellationToken);
                    received = prefix.Length;
                    progress?.Report((received, total));
                }
                byte[] buffer = new byte[81920];
                int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    received += count;
                    if (progressTimer.ElapsedMilliseconds >= 100 || received == total)
                    {
                        progress?.Report((received, total));
                        progressTimer.Restart();
                    }
                }
            }

            if (received == 0 || (total.HasValue && total != received))
            {
                throw new IOException(Strings.WebDownloadIncomplete);
            }

            cancellationToken.ThrowIfCancellationRequested();
            for (int suffix = 0; ; suffix++)
            {
                string candidate = suffix == 0 ? name
                    : $"{Path.GetFileNameWithoutExtension(name)} ({suffix}){Path.GetExtension(name)}";
                string destination = Path.Combine(directory, candidate);
                try
                {
                    File.Move(temporaryPath, destination, overwrite: false);
                    return destination;
                }
                catch (IOException) when (File.Exists(destination))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, Uri? referrer, IReadOnlyList<Cookie> cookies,
        CancellationToken cancellationToken)
    {
        // The native API omits SameSite/partition metadata. Do not import another site's
        // existing login session merely because the download redirects to that site.
        CookieContainer cookieContainer = BrowserSessionCookies.CreateContainer(cookies, referrer ?? uri);
        for (int redirects = 0; ; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Referrer = NormalizeReferrer(referrer, uri);
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
                if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Found
                    or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
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

    private static bool LooksLikeHtml(byte[] prefix)
    {
        Encoding encoding = Encoding.UTF8;
        if (prefix.AsSpan().StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) encoding = Encoding.UTF32;
        else if (prefix.AsSpan().StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) encoding = new UTF32Encoding(true, true);
        else if (prefix.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) encoding = Encoding.Unicode;
        else if (prefix.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) encoding = Encoding.BigEndianUnicode;
        ReadOnlySpan<char> text = encoding.GetString(prefix).AsSpan().TrimStart('\uFEFF').TrimStart();
        bool comment = false;
        while (true)
        {
            if (text.StartsWith("<!--"))
            {
                int end = text.IndexOf("-->");
                if (end < 0) return true;
                comment = true;
                text = text[(end + 3)..].TrimStart();
            }
            else if (text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
            {
                int end = text.IndexOf("?>");
                if (end < 0) return false;
                text = text[(end + 2)..].TrimStart();
            }
            else break;
        }
        if (text.IsEmpty) return comment;
        foreach (string tag in new[] { "<!doctype html", "<html", "<head", "<body", "<script", "<iframe", "<title", "<div", "<h1", "<table", "<p", "<font", "<a", "<style", "<b", "<br", "<form", "<meta" })
        {
            if (text.StartsWith(tag, StringComparison.OrdinalIgnoreCase)
                && (text.Length == tag.Length || char.IsWhiteSpace(text[tag.Length]) || text[tag.Length] is '>' or '/')) return true;
        }
        return false;
    }

    internal static string CreateFileName(string? suggestedName, Uri uri, string? mediaType)
    {
        string name = NormalizeFileName(suggestedName)
            ?? NormalizeFileName(Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath)))
            ?? "media";
        if (name.Length > 160) name = name[..140] + Path.GetExtension(name);

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
        else if ((mediaType == null || mediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
                 && IsMediaExtension(Path.GetExtension(name)))
        {
            // Generic binary downloads are accepted only when their filename identifies media.
        }
        else
        {
            if (string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(Strings.WebDownloadHtmlResponse);
            throw new InvalidOperationException(Strings.WebDownloadUnsupported);
        }

        return name;
    }

    private static string? NormalizeFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string name = Path.GetFileName(value.Trim().Trim('"').Replace('\\', '/'));
        name = string.Concat(name.Select(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c) ? '_' : c)).Trim(' ', '.');
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
