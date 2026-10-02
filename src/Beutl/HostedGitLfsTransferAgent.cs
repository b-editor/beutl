using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beutl;

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
        Func<string, long> fileLength, Func<string, Stream> openRead)
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
                        string path = await DownloadAsync(message, url, http, output, cancellationToken);
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
        if (url.AbsolutePath.EndsWith("/multipart", StringComparison.Ordinal))
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
        if (message.Size <= 5L * 1024 * 1024 * 1024)
            throw new InvalidOperationException("Multipart is reserved for objects above 5 GiB");
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
            Uri partUrl = new(url.AbsoluteUri.TrimEnd('/') + $"/parts/{currentPart}");
            await SendWithRetryAsync(http, () =>
            {
                var request = NewRequest(HttpMethod.Put, partUrl, message.Action!.Header);
                Stream stream = openRead(message.Path!);
                stream.Seek((long)(currentPart - 1) * status.PartSize, SeekOrigin.Begin);
                request.Content = new StreamContent(new SliceStream(stream, length));
                request.Content.Headers.ContentLength = length;
                return request;
            }, cancellationToken);
            progress += length;
            await ProgressAsync(output, message.Oid!, progress, length);
        }
        Uri completeUrl = new(url.AbsoluteUri.TrimEnd('/') + "/complete");
        using JsonDocument completedUpload = await SendJsonWithRetryAsync(http,
            () => NewRequest(HttpMethod.Post, completeUrl, message.Action!.Header), cancellationToken);
        if (completedUpload.RootElement.GetProperty("oid").GetString() != message.Oid ||
            completedUpload.RootElement.GetProperty("size").GetInt64() != message.Size)
            throw new InvalidOperationException("Multipart verification response does not match the LFS object");
        if (progress < message.Size) await ProgressAsync(output, message.Oid!, message.Size, message.Size - progress);
    }

    private static async Task<string> DownloadAsync(
        Message message, Uri url, HttpClient http, TextWriter output, CancellationToken cancellationToken)
    {
        string path = Path.Combine(Path.GetTempPath(), $"beutl-lfs-{Guid.NewGuid():N}");
        try
        {
            await using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, bufferSize: 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] buffer = new byte[128 * 1024];
            long received = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            for (int attempt = 0; received < message.Size && attempt < 5; attempt++)
            {
                try
                {
                    using HttpRequestMessage request = NewRequest(HttpMethod.Get, url, message.Action!.Header);
                    if (received > 0) request.Headers.Range = new RangeHeaderValue(received, null);
                    using HttpResponseMessage response = await http.SendAsync(
                        request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    if (received > 0 && (response.StatusCode != HttpStatusCode.PartialContent
                        || response.Content.Headers.ContentRange?.From != received))
                    {
                        throw new InvalidOperationException("Storage did not honor the LFS download resume range");
                    }
                    await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
                    while (received < message.Size)
                    {
                        int count = await source.ReadAsync(buffer, cancellationToken);
                        if (count == 0) break;
                        received += count;
                        if (received > message.Size)
                            throw new InvalidOperationException("LFS download exceeds expected size");
                        hash.AppendData(buffer.AsSpan(0, count));
                        await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                        await ProgressAsync(output, message.Oid!, received, count);
                    }
                    if (received == message.Size)
                    {
                        if (await source.ReadAsync(buffer.AsMemory(0, 1), cancellationToken) != 0)
                            throw new InvalidOperationException("LFS download exceeds expected size");
                        break;
                    }
                }
                catch (Exception ex) when (
                    attempt < 4 && (ex is IOException ||
                        ex is HttpRequestException { StatusCode: null } ||
                        ex is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } ||
                        ex is HttpRequestException { StatusCode: >= HttpStatusCode.InternalServerError }))
                {
                    await Task.Delay(TimeSpan.FromSeconds(1 << attempt), cancellationToken);
                }
            }
            if (received != message.Size ||
                !Convert.ToHexStringLower(hash.GetHashAndReset()).Equals(message.Oid, StringComparison.Ordinal))
                throw new InvalidOperationException("LFS download SHA-256 or size mismatch");
            return path;
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

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
