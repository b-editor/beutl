using System.Net.Http;

namespace Beutl.Editor.Components.WebBrowserTab;

internal sealed class BrowserMediaDownload(HttpClient client)
{
    internal static readonly BrowserMediaDownload Default = new(new HttpClient { Timeout = TimeSpan.FromSeconds(30) });

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
        IProgress<(long Received, long? Total)>? progress, CancellationToken cancellationToken, Uri? referrer = null)
    {
        if (!IsHttpUri(uri))
        {
            throw new InvalidOperationException(Strings.WebDownloadUnsupported);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Referrer = NormalizeReferrer(referrer, request.RequestUri!);
        using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        Uri finalUri = response.RequestMessage?.RequestUri ?? uri;
        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        string? nameHint = NormalizeFileName(response.Content.Headers.ContentDisposition?.FileNameStar)
            ?? NormalizeFileName(response.Content.Headers.ContentDisposition?.FileName)
            ?? NormalizeFileName(suggestedName);
        string name = CreateFileName(nameHint, finalUri, mediaType);
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $".{Guid.NewGuid():N}.part");
        try
        {
            long received = 0;
            var progressTimer = Stopwatch.StartNew();
            long? total = response.Content.Headers.ContentLength;
            await using (Stream input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             81920, FileOptions.Asynchronous))
            {
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
