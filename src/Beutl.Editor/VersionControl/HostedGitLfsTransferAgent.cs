using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beutl.Editor.VersionControl;

/// <summary>
/// Git LFS custom transfer protocol. The regular Beutl executable enters
/// this headless mode before any UI or configuration is initialized.
/// </summary>
internal static class HostedGitLfsTransferAgent
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);
    private static readonly HttpClient s_http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private sealed record ActionInfo(string Href, Dictionary<string, string>? Header);
    private sealed record Message(
        string Event, string? Operation, string? Oid, long Size,
        string? Path, ActionInfo? Action);
    private sealed record UploadedPart(int PartNumber, string Etag, long Size);
    private sealed record StartResult(bool Complete, int PartSize, int PartCount, UploadedPart[]? Parts);

    public static async Task<int> RunAsync()
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, args) => { args.Cancel = true; cancellation.Cancel(); };
        return await RunAsync(Console.In, Console.Out, s_http, cancellation.Token);
    }

    internal static Task<int> RunAsync(
        TextReader input, TextWriter output, HttpClient http, CancellationToken cancellationToken) =>
        RunAsync(input, output, http, cancellationToken,
            static path => new FileInfo(path).Length,
            static path => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan));

    internal static async Task<int> RunAsync(
        TextReader input, TextWriter output, HttpClient http, CancellationToken cancellationToken,
        Func<string, long> fileLength, Func<string, Stream> openRead,
        Func<string, Stream>? openWrite = null, TimeProvider? timeProvider = null,
        string? downloadCacheDirectory = null)
    {
        try
        {
            string? line = await input.ReadLineAsync(cancellationToken);
            Message? init = JsonSerializer.Deserialize<Message>(line ?? "", s_json);
            if (init?.Event != "init" || init.Operation is not ("upload" or "download"))
            {
                await WriteAsync(output, new { error = new { code = 2, message = "Invalid Git LFS initiation" } });
                return 1;
            }
            await WriteAsync(output, new { });
            while ((line = await input.ReadLineAsync(cancellationToken)) is not null)
            {
                Message? message = JsonSerializer.Deserialize<Message>(line, s_json);
                if (message?.Event == "terminate") return 0;
                string oid = message?.Oid ?? "";
                try
                {
                    if (message?.Event != init.Operation ||
                        !System.Text.RegularExpressions.Regex.IsMatch(oid, "^[0-9a-f]{64}$") ||
                        message.Size < 0 || message.Action is null ||
                        !Uri.TryCreate(message.Action.Href, UriKind.Absolute, out Uri? url) ||
                        url.Scheme != Uri.UriSchemeHttps)
                    {
                        throw new InvalidOperationException("Invalid Git LFS transfer action");
                    }
                    if (message.Event == "upload")
                    {
                        await UploadAsync(message, url, http, output, fileLength, openRead, cancellationToken);
                        await WriteAsync(output, new { @event = "complete", oid });
                    }
                    else
                    {
                        string path = await DownloadAsync(message, url, http, output, cancellationToken,
                            openWrite, timeProvider ?? TimeProvider.System, downloadCacheDirectory);
                        await WriteAsync(output, new { @event = "complete", oid, path });
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    await WriteAsync(output, new { @event = "complete", oid, error = new { code = 2, message = ex.Message } });
                }
            }
            return 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task UploadAsync(
        Message message, Uri url, HttpClient http, TextWriter output,
        Func<string, long> fileLength, Func<string, Stream> openRead, CancellationToken cancellationToken)
    {
        string path = message.Path ?? throw new InvalidOperationException("Upload file path is missing");
        if (fileLength(path) != message.Size)
            throw new InvalidOperationException("The LFS upload file size changed");
        if (url.AbsolutePath.EndsWith("/tus", StringComparison.Ordinal))
            await UploadTusAsync(message, url, http, output, openRead, cancellationToken);
        else if (url.AbsolutePath.EndsWith("/multipart", StringComparison.Ordinal))
            await UploadMultipartAsync(message, url, http, output, openRead, cancellationToken);
        else
            await UploadBasicAsync(message, url, http, output, openRead, cancellationToken);
    }

    private static async Task UploadBasicAsync(
        Message message, Uri url, HttpClient http, TextWriter output,
        Func<string, Stream> openRead, CancellationToken cancellationToken)
    {
        await SendWithRetryAsync(http, () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Put, url);
            Stream stream = openRead(message.Path!);
            request.Content = new StreamContent(stream);
            request.Content.Headers.ContentLength = message.Size;
            AddActionHeaders(request, message.Action!.Header);
            return request;
        }, cancellationToken);
        await ProgressAsync(output, message.Oid!, message.Size, message.Size);
    }

    private static async Task UploadMultipartAsync(
        Message message, Uri url, HttpClient http, TextWriter output,
        Func<string, Stream> openRead, CancellationToken cancellationToken)
    {
        using JsonDocument started = await SendJsonWithRetryAsync(http,
            () => NewRequest(HttpMethod.Post, url, message.Action!.Header), cancellationToken);
        StartResult status = started.Deserialize<StartResult>(s_json)
            ?? throw new InvalidOperationException("Invalid multipart start response");
        if (status.Complete) return;
        if (status.PartSize != 64 * 1024 * 1024 || status.PartCount <= 0 || status.PartCount > 10_000 ||
            (message.Size + status.PartSize - 1) / status.PartSize != status.PartCount)
            throw new InvalidOperationException("Invalid multipart part layout");
        var completed = (status.Parts ?? []).ToDictionary(part => part.PartNumber);
        long progress = completed.Values.Sum(part => part.Size);
        if (progress > 0) await ProgressAsync(output, message.Oid!, progress, progress);
        for (int partNumber = 1; partNumber <= status.PartCount; partNumber++)
        {
            long length = Math.Min(status.PartSize, message.Size - (long)(partNumber - 1) * status.PartSize);
            if (completed.TryGetValue(partNumber, out UploadedPart? part))
            {
                if (part.Size != length) throw new InvalidOperationException("Invalid resumed part size");
                continue;
            }
            int currentPart = partNumber;
            Uri partUrl = new UriBuilder(url) { Path = url.AbsolutePath.TrimEnd('/') + $"/parts/{currentPart}" }.Uri;
            await SendWithRetryAsync(http, () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Put, partUrl);
                Stream stream = openRead(message.Path!);
                stream.Seek((long)(currentPart - 1) * status.PartSize, SeekOrigin.Begin);
                request.Content = new StreamContent(new SliceStream(stream, length));
                request.Content.Headers.ContentLength = length;
                AddActionHeaders(request, message.Action!.Header);
                return request;
            }, cancellationToken);
            progress += length;
            await ProgressAsync(output, message.Oid!, progress, length);
        }
        Uri completeUrl = new UriBuilder(url) { Path = url.AbsolutePath.TrimEnd('/') + "/complete" }.Uri;
        using JsonDocument completedUpload = await WaitMultipartCompletionAsync(
            http, completeUrl, message.Action!.Header, cancellationToken);
        if (completedUpload.RootElement.GetProperty("oid").GetString() != message.Oid ||
            completedUpload.RootElement.GetProperty("size").GetInt64() != message.Size)
            throw new InvalidOperationException("Multipart verification response does not match the LFS object");
        if (progress < message.Size) await ProgressAsync(output, message.Oid!, message.Size, message.Size - progress);
    }

    private static async Task UploadTusAsync(
        Message message, Uri creationUrl, HttpClient http, TextWriter output,
        Func<string, Stream> openRead, CancellationToken cancellationToken)
    {
        using HttpResponseMessage created = await SendTusRequestWithRetryAsync(http, () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, creationUrl);
            request.Headers.TryAddWithoutValidation("Tus-Resumable", "1.0.0");
            request.Headers.TryAddWithoutValidation("Upload-Length",
                message.Size.ToString(System.Globalization.CultureInfo.InvariantCulture));
            request.Content = new ByteArrayContent([]);
            AddActionHeaders(request, message.Action!.Header);
            return request;
        }, cancellationToken);
        if (created.StatusCode != HttpStatusCode.Created || created.Headers.Location is null)
            throw new InvalidOperationException("Invalid tus creation response");
        CheckTusVersion(created);
        Uri uploadUrl = new(creationUrl, created.Headers.Location);
        if (uploadUrl.Scheme != Uri.UriSchemeHttps ||
            uploadUrl.Host != creationUrl.Host || uploadUrl.Port != creationUrl.Port ||
            !string.IsNullOrEmpty(uploadUrl.UserInfo) ||
            !string.IsNullOrEmpty(uploadUrl.Query) || !string.IsNullOrEmpty(uploadUrl.Fragment) ||
            !uploadUrl.AbsolutePath.StartsWith(creationUrl.AbsolutePath.TrimEnd('/') + "/",
                StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid tus upload URL");

        long offset = await GetTusOffsetAsync(http, uploadUrl, message.Action!.Header,
            message.Size, cancellationToken);
        if (offset > 0) await ProgressAsync(output, message.Oid!, offset, offset);
        while (offset < message.Size)
        {
            long length = Math.Min(64L * 1024 * 1024, message.Size - offset);
            long before = offset;
            bool advanced = false;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                HttpResponseMessage? response = null;
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Patch, uploadUrl);
                    request.Headers.TryAddWithoutValidation("Tus-Resumable", "1.0.0");
                    request.Headers.TryAddWithoutValidation("Upload-Offset",
                        before.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    Stream stream = openRead(message.Path!);
                    stream.Seek(before, SeekOrigin.Begin);
                    request.Content = new StreamContent(new SliceStream(stream, length));
                    request.Content.Headers.ContentLength = length;
                    AddActionHeaders(request, message.Action!.Header);
                    request.Content.Headers.ContentType =
                        new MediaTypeHeaderValue("application/offset+octet-stream");
                    response = await http.SendAsync(request,
                        HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        CheckTusVersion(response);
                        long acknowledged = ReadTusOffset(response);
                        if (acknowledged == before + length)
                        {
                            offset = acknowledged;
                            advanced = true;
                            break;
                        }
                    }
                    else if (response.StatusCode != HttpStatusCode.Conflict &&
                             !Retryable(response.StatusCode))
                    {
                        throw new InvalidOperationException(
                            $"tus PATCH failed: HTTP {(int)response.StatusCode}");
                    }
                }
                catch (HttpRequestException)
                {
                    // The server may have accepted the part before the response
                    // was lost. HEAD is authoritative before sending it again.
                }
                finally
                {
                    response?.Dispose();
                }
                long reported = await GetTusOffsetAsync(http, uploadUrl, message.Action!.Header,
                    message.Size, cancellationToken);
                if (reported < before)
                    throw new InvalidOperationException("tus offset moved backwards");
                if (reported > before)
                {
                    offset = reported;
                    advanced = true;
                    break;
                }
                if (attempt == 4)
                    throw new InvalidOperationException("tus PATCH did not advance");
                await Task.Delay(TimeSpan.FromSeconds(1 << attempt), cancellationToken);
            }
            if (!advanced || offset <= before)
                throw new InvalidOperationException("tus upload did not advance");
            await ProgressAsync(output, message.Oid!, offset, offset - before);
        }
        await WaitTusVerificationAsync(http, uploadUrl, message.Action!.Header,
            message.Size, cancellationToken);
    }

    private static async Task WaitTusVerificationAsync(
        HttpClient http, Uri uploadUrl, Dictionary<string, string>? headers,
        long size, CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow.AddHours(24);
        while (true)
        {
            using HttpResponseMessage response = await SendTusRequestWithRetryAsync(http, () =>
            {
                var request = NewRequest(HttpMethod.Head, uploadUrl, headers);
                request.Headers.TryAddWithoutValidation("Tus-Resumable", "1.0.0");
                return request;
            }, cancellationToken);
            CheckTusVersion(response);
            if (ReadTusNumber(response, "Upload-Length") != size || ReadTusOffset(response) != size)
                throw new InvalidOperationException("tus verification offset changed");
            if (!response.Headers.TryGetValues("Upload-Verified", out IEnumerable<string>? values))
                throw new InvalidOperationException("tus verification status is missing");
            string? verified = values.SingleOrDefault();
            if (verified == "true") return;
            if (verified != "false")
                throw new InvalidOperationException("Invalid tus verification status");
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("tus verification did not finish before the upload expired");
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }

    private static async Task<JsonDocument> WaitMultipartCompletionAsync(
        HttpClient http, Uri completeUrl, Dictionary<string, string>? headers,
        CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow.AddHours(24);
        while (true)
        {
            JsonDocument result = await SendJsonWithRetryAsync(http,
                () => NewRequest(HttpMethod.Post, completeUrl, headers), cancellationToken);
            if (!result.RootElement.TryGetProperty("verifying", out JsonElement verifying) ||
                verifying.ValueKind != JsonValueKind.True) return result;
            result.Dispose();
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Multipart verification did not finish before the upload expired");
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }

    private static async Task<long> GetTusOffsetAsync(
        HttpClient http, Uri uploadUrl, Dictionary<string, string>? headers,
        long size, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendTusRequestWithRetryAsync(http, () =>
        {
            var request = NewRequest(HttpMethod.Head, uploadUrl, headers);
            request.Headers.TryAddWithoutValidation("Tus-Resumable", "1.0.0");
            return request;
        }, cancellationToken);
        if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.NoContent))
            throw new InvalidOperationException("Invalid tus HEAD response");
        CheckTusVersion(response);
        long length = ReadTusNumber(response, "Upload-Length");
        long offset = ReadTusOffset(response);
        if (length != size || offset > size)
            throw new InvalidOperationException("tus upload length or offset changed");
        return offset;
    }

    private static long ReadTusOffset(HttpResponseMessage response) =>
        ReadTusNumber(response, "Upload-Offset");

    private static long ReadTusNumber(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out IEnumerable<string>? values) ||
            !long.TryParse(values.SingleOrDefault(), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out long value) || value < 0)
            throw new InvalidOperationException($"Invalid tus {name} response");
        return value;
    }

    private static void CheckTusVersion(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Tus-Resumable", out IEnumerable<string>? values) ||
            values.SingleOrDefault() != "1.0.0")
            throw new InvalidOperationException("Unsupported tus version");
    }

    private static async Task<HttpResponseMessage> SendTusRequestWithRetryAsync(
        HttpClient http, Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            using HttpRequestMessage request = requestFactory();
            try
            {
                HttpResponseMessage response = await http.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.IsSuccessStatusCode) return response;
                HttpStatusCode status = response.StatusCode;
                response.Dispose();
                if (attempt >= 4 || !Retryable(status))
                    throw new InvalidOperationException($"tus request failed: HTTP {(int)status}");
            }
            catch (HttpRequestException) when (attempt < 4) { }
            await Task.Delay(TimeSpan.FromSeconds(1 << attempt), cancellationToken);
        }
    }

    private static async Task<string> DownloadAsync(
        Message message, Uri url, HttpClient http, TextWriter output, CancellationToken cancellationToken,
        Func<string, Stream>? openWrite, TimeProvider timeProvider, string? downloadCacheDirectory)
    {
        downloadCacheDirectory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Beutl", "git-lfs-downloads", Convert.ToHexStringLower(SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(Environment.CurrentDirectory)))));
        Directory.CreateDirectory(downloadCacheDirectory);
        PruneExpiredDownloads(downloadCacheDirectory);
        string path = Path.Combine(downloadCacheDirectory, message.Oid + ".partial");
        // Keep ownership until cleanup or the verified file has been moved.
        // A second agent for this OID must not delete or change our partial file.
        await using var cacheLock = new FileStream(Path.Combine(downloadCacheDirectory, message.Oid + ".lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1, FileOptions.DeleteOnClose);
        bool ownsPartial = false;
        try
        {
            await using (Stream destination = openWrite?.Invoke(path) ?? new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None, bufferSize: 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                ownsPartial = true;
                byte[] buffer = new byte[128 * 1024];
                if (destination.Length > message.Size) destination.SetLength(0);
                long received = destination.Length;
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                destination.Position = 0;
                for (long hashed = 0; hashed < received;)
                {
                    int count = await destination.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, received - hashed)), cancellationToken);
                    if (count == 0) throw new IOException("The cached LFS download changed while being read");
                    hash.AppendData(buffer.AsSpan(0, count));
                    hashed += count;
                }
                destination.Position = received;
                int failures = 0;
                int smallResponses = 0;
                long progressCheckpoint = 0;
                long minimumProgress = Math.Min(buffer.Length, Math.Max(1, message.Size / 16));
                async Task<T> WithIdleTimeoutAsync<T>(Func<CancellationToken, Task<T>> operation)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2), timeProvider);
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                    try { return await operation(idle.Token); }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new IOException("LFS download stalled");
                    }
                }
                while (received < message.Size && failures < 5)
                {
                    long previousReceived = received;
                    bool readingFromNetwork = true;
                    bool transportFailed = false;
                    try
                    {
                        using HttpRequestMessage request = NewRequest(HttpMethod.Get, url, message.Action!.Header);
                        if (received > 0) request.Headers.Range = new RangeHeaderValue(received, null);
                        using HttpResponseMessage response = await WithIdleTimeoutAsync(
                            token => http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token));
                        if (!response.IsSuccessStatusCode)
                            throw new HttpRequestException($"LFS download failed: HTTP {(int)response.StatusCode}",
                                null, response.StatusCode);
                        if (received > 0 && (response.StatusCode != HttpStatusCode.PartialContent
                            || response.Content.Headers.ContentRange?.From != received))
                        {
                            throw new InvalidOperationException("Storage did not honor the LFS download resume range");
                        }
                        await using Stream source = await WithIdleTimeoutAsync(token => response.Content.ReadAsStreamAsync(token));
                        readingFromNetwork = false;
                        while (received < message.Size)
                        {
                            readingFromNetwork = true;
                            int count = await WithIdleTimeoutAsync(async token => await source.ReadAsync(buffer, token));
                            readingFromNetwork = false;
                            if (count == 0) break;
                            if (count > message.Size - received)
                                throw new InvalidOperationException("LFS download exceeds expected size");
                            await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                            received += count;
                            hash.AppendData(buffer.AsSpan(0, count));
                            await ProgressAsync(output, message.Oid!, received, count);
                        }
                        if (received == message.Size)
                        {
                            readingFromNetwork = true;
                            if (await WithIdleTimeoutAsync(async token => await source.ReadAsync(buffer.AsMemory(0, 1), token)) != 0)
                                throw new InvalidOperationException("LFS download exceeds expected size");
                            break;
                        }
                    }
                    catch (Exception ex) when (
                        readingFromNetwork && received < message.Size && (ex is IOException ||
                            ex is HttpRequestException { StatusCode: null } ||
                            ex is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } ||
                            ex is HttpRequestException { StatusCode: >= HttpStatusCode.InternalServerError }))
                    {
                        transportFailed = true;
                    }
                    if (received - progressCheckpoint >= minimumProgress)
                    {
                        progressCheckpoint = received;
                        smallResponses = 0;
                    }
                    else
                    {
                        smallResponses++;
                    }
                    // A valid short range is progress, regardless of request count.
                    // Bound stalled retries and back off tiny responses without
                    // imposing a minimum transfer speed on advancing downloads.
                    failures = received > previousReceived ? 0 : failures + 1;
                    if (failures >= 5)
                        throw new DownloadInterruptedException();
                    if (transportFailed || failures > 0 || smallResponses > 0)
                        await Task.Delay(TimeSpan.FromSeconds(1 << Math.Clamp(Math.Max(failures, smallResponses) - 1, 0, 3)),
                            timeProvider, cancellationToken);
                }
                if (received != message.Size ||
                    !Convert.ToHexStringLower(hash.GetHashAndReset()).Equals(message.Oid, StringComparison.Ordinal))
                    throw new InvalidOperationException("LFS download SHA-256 or size mismatch");
                await destination.FlushAsync(cancellationToken);
            }
            string completedPath = Path.Combine(Path.GetTempPath(), $"beutl-lfs-{Guid.NewGuid():N}");
            File.Move(path, completedPath);
            return completedPath;
        }
        catch (Exception ex) when (ex is OperationCanceledException or DownloadInterruptedException ||
            ex is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden })
        {
            // A subsequent Git LFS invocation resumes this OID and
            // rehashes its prefix. Never persist the URL or action headers.
            if (ownsPartial && new FileInfo(path).Length == 0) File.Delete(path);
            throw;
        }
        catch
        {
            if (ownsPartial) File.Delete(path);
            throw;
        }
    }

    private static void PruneExpiredDownloads(string directory)
    {
        DateTime cutoff = DateTime.UtcNow.AddHours(-24);
        foreach (string path in Directory.EnumerateFiles(directory, "*.partial"))
        {
            if (File.GetLastWriteTimeUtc(path) >= cutoff) continue;
            try
            {
                using var cacheLock = new FileStream(Path.ChangeExtension(path, ".lock"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1, FileOptions.DeleteOnClose);
                if (File.GetLastWriteTimeUtc(path) < cutoff) File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An active transfer owns the file, or cleanup is unavailable.
            }
        }
        foreach (string path in Directory.EnumerateFiles(directory, "*.lock"))
        {
            if (File.GetLastWriteTimeUtc(path) >= cutoff) continue;
            try
            {
                using var cacheLock = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                    FileShare.None, bufferSize: 1, FileOptions.DeleteOnClose);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed class DownloadInterruptedException() : IOException("LFS download made no progress after five attempts") { }

    private static HttpRequestMessage NewRequest(HttpMethod method, Uri url, Dictionary<string, string>? headers)
    {
        var request = new HttpRequestMessage(method, url);
        AddActionHeaders(request, headers);
        return request;
    }

    private static void AddActionHeaders(HttpRequestMessage request, Dictionary<string, string>? headers)
    {
        foreach ((string key, string value) in headers ?? [])
        {
            if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
            if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                request.Content ??= new ByteArrayContent([]);
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(value);
            }
            else if (!request.Headers.TryAddWithoutValidation(key, value))
            {
                request.Content ??= new ByteArrayContent([]);
                request.Content.Headers.TryAddWithoutValidation(key, value);
            }
        }
    }

    private static async Task SendWithRetryAsync(
        HttpClient http, Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            using HttpRequestMessage request = requestFactory();
            try
            {
                using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode) return;
                if (attempt >= 4 || !Retryable(response.StatusCode))
                    throw new InvalidOperationException($"LFS transfer failed: HTTP {(int)response.StatusCode}");
            }
            catch (HttpRequestException) when (attempt < 4) { }
            await Task.Delay(TimeSpan.FromSeconds(1 << attempt), cancellationToken);
        }
    }

    private static async Task<JsonDocument> SendJsonWithRetryAsync(
        HttpClient http, Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            using HttpRequestMessage request = requestFactory();
            try
            {
                using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                    return await JsonDocument.ParseAsync(
                        await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                if (attempt >= 4 || !Retryable(response.StatusCode))
                    throw new InvalidOperationException($"LFS transfer failed: HTTP {(int)response.StatusCode}");
            }
            catch (HttpRequestException) when (attempt < 4) { }
            await Task.Delay(TimeSpan.FromSeconds(1 << attempt), cancellationToken);
        }
    }

    private static bool Retryable(HttpStatusCode status) => status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    private static Task ProgressAsync(TextWriter output, string oid, long total, long delta) =>
        WriteAsync(output, new { @event = "progress", oid, bytesSoFar = total, bytesSinceLast = delta });

    private static async Task WriteAsync(TextWriter output, object value)
    {
        await output.WriteLineAsync(JsonSerializer.Serialize(value, s_json));
        await output.FlushAsync();
    }

    private sealed class SliceStream(Stream source, long length) : Stream
    {
        private readonly long _length = length;
        private long _remaining = length;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get => _length - _remaining; set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_remaining == 0) return 0;
            int count = await source.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _remaining)], cancellationToken);
            _remaining -= count;
            return count;
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining == 0) return 0;
            int read = source.Read(buffer, offset, (int)Math.Min(count, _remaining));
            _remaining -= read;
            return read;
        }
        protected override void Dispose(bool disposing) { if (disposing) source.Dispose(); base.Dispose(disposing); }
        public override ValueTask DisposeAsync() => source.DisposeAsync();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
