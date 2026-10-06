using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace Beutl.Editor.VersionControl;

/// <summary>
/// The "beutl-tus" Git LFS custom transfer agent. Beutl hosted Git limits a plain LFS upload to
/// one 5 GB storage PUT; this agent sends any size as resumable tus parts instead, several at a
/// time when hosted Git takes them out of order. The regular Beutl executable enters this headless
/// mode before any UI is initialized, and Beutl offers the agent to Git only for its own pushes, so
/// a plain Git client keeps working without it.
/// </summary>
internal static partial class HostedGitLfsTransferAgent
{
    public const string CommandLineFlag = "--git-lfs-transfer";
    private const string TransferName = "beutl-tus";
    // Each PATCH becomes one storage part; the server accepts at most this much per request.
    internal const long PartSize = 32L * 1024 * 1024;
    // One connection is limited by its own round trips; four measured four times as fast.
    internal const int ConcurrentParts = 4;
    // Hosted Git takes parts out of order when it answers with this header and the part size.
    private const string PartSizeHeader = "Beutl-Part-Size";
    // The SHA-256 chaining state of the bytes before a part, which hosted Git checks.
    private const string HashStateHeader = "Beutl-Sha256-State";
    // Divides a part, so every read but the file's last is whole SHA-256 blocks.
    private const int HashBufferSize = 1024 * 1024;
    private const int MaxAttempts = 5;
    internal static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(2);
    private const int MaxFailureDetailBytes = 1024;
    internal static readonly TimeSpan FailureDetailTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    private sealed record ActionInfo(string? Href, Dictionary<string, string>? Header);

    private sealed record Message(
        string? Event, string? Operation, string? Oid, long Size, string? Path, ActionInfo? Action);

    /// <summary>The accepted offset, whether the object is published, and the SHA-256 state at the offset if hosted Git sent it.</summary>
    private readonly record struct TusState(long Offset, bool Verified, string? HashState = null);

    /// <summary>One tus upload: where its parts go, the file they are read from, and the clock its retries wait on.</summary>
    private sealed record TusUpload(
        HttpClient Http, Uri Url, Dictionary<string, string>? Headers, long Size, Func<Stream> OpenFile, TimeProvider Time)
    {
        public long PartLength(long offset) => Math.Min(PartSize, Size - offset);
    }

    /// <summary>One PATCH: whether hosted Git stored the part (with the state it answered, when read), or what the next attempt needs.</summary>
    private readonly record struct PatchAttempt(
        bool Stored, TusState State, TimeSpan? RetryAfter, HttpRequestException? TransportFailure);

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex OidPattern();

    /// <summary>
    /// Process-scoped Git configuration that offers this agent for LFS uploads. Hosted Git selects
    /// it; servers that do not know the name ignore it and keep their usual transfer.
    /// </summary>
    public static IReadOnlyList<string> GitConfigArguments(string? processPath, string? entryAssemblyPath)
    {
        if (string.IsNullOrEmpty(processPath)) return [];
        string arguments = CommandLineFlag;
        // A development run through the dotnet host needs the application assembly as well.
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(entryAssemblyPath)) return [];
            arguments = $"{ShellQuote(entryAssemblyPath)} {CommandLineFlag}";
        }

        return
        [
            "-c", $"lfs.customtransfer.{TransferName}.path={processPath}",
            // Git LFS expands the arguments with a shell.
            "-c", $"lfs.customtransfer.{TransferName}.args={arguments}",
            "-c", $"lfs.customtransfer.{TransferName}.direction=upload",
        ];
    }

    private static string ShellQuote(string value) => $"'{value.Replace("'", @"'\''", StringComparison.Ordinal)}'";

    public static async Task<int> RunAsync()
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, args) =>
        {
            args.Cancel = true;
            cancellation.Cancel();
        };
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var input = new StreamReader(Console.OpenStandardInput(), utf8, detectEncodingFromByteOrderMarks: false);
        using var output = new StreamWriter(Console.OpenStandardOutput(), utf8) { NewLine = "\n" };
        using var http = new HttpClient(CreateHandler()) { Timeout = Timeout.InfiniteTimeSpan };
        return await RunAsync(input, output, http, OpenFile, TimeProvider.System, cancellation.Token);
    }

    // A followed redirect would carry the action's credential headers past the origin check.
    internal static SocketsHttpHandler CreateHandler() => new() { AllowAutoRedirect = false };

    private static Stream OpenFile(string path) => new FileStream(
        path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 128 * 1024,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    internal static async Task<int> RunAsync(
        TextReader input, TextWriter output, HttpClient http, Func<string, Stream> openRead,
        TimeProvider time, CancellationToken cancellationToken)
    {
        Message? init = Parse(await input.ReadLineAsync(cancellationToken));
        if (init is not { Event: "init", Operation: "upload" })
        {
            await WriteAsync(output, new { error = new { code = 32, message = "Beutl transfers Git LFS uploads only" } });
            return 1;
        }

        await WriteAsync(output, new { });
        while (await input.ReadLineAsync(cancellationToken) is { } line)
        {
            Message? message = Parse(line);
            if (message?.Event == "terminate") break;
            string oid = message?.Oid ?? "";
            try
            {
                await UploadAsync(message, http, output, openRead, time, cancellationToken);
                await WriteAsync(output, new { @event = "complete", oid });
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is not OutOfMemoryException)
            {
                await WriteAsync(output, new { @event = "complete", oid, error = new { code = 2, message = ex.Message } });
            }
        }

        return 0;
    }

    private static Message? Parse(string? line)
    {
        if (line is null) return null;
        try
        {
            return JsonSerializer.Deserialize<Message>(line, s_json);
        }
        catch (JsonException)
        {
            // A malformed request is reported for its object like any other invalid request.
            return null;
        }
    }

    private static async Task UploadAsync(
        Message? message, HttpClient http, TextWriter output, Func<string, Stream> openRead,
        TimeProvider time, CancellationToken cancellationToken)
    {
        if (message is not { Event: "upload", Oid: { } oid, Path: { } path, Action: { Href: { } href } action }
            || !OidPattern().IsMatch(oid) || message.Size < 0)
        {
            throw new InvalidOperationException("Invalid Git LFS upload request");
        }

        Uri creation = TransferUri(href);
        await using (Stream file = openRead(path))
        {
            if (file.Length != message.Size)
                throw new InvalidOperationException("The LFS file changed size before its upload");
        }

        // Hosted Git keeps one upload per object, so creation also finds an interrupted one.
        (Uri uploadUrl, bool parallel) = await CreateAsync(http, creation, action.Header, message.Size, time, cancellationToken);
        var upload = new TusUpload(http, uploadUrl, action.Header, message.Size, () => openRead(path), time);
        TusState state = await GetStateAsync(upload, cancellationToken);
        if (state.Offset > 0) await ProgressAsync(output, oid, state.Offset, state.Offset);
        // Parts numbered by offset need every part accepted so far to be a whole one.
        if (parallel && state.Offset % PartSize == 0 && state.Offset < message.Size)
        {
            await UploadPartsAsync(upload, oid, state, output, cancellationToken);
            state = await GetStateAsync(upload, cancellationToken);
            if (state.Offset != message.Size) throw new InvalidOperationException("Hosted Git did not accept every LFS part");
        }

        while (state.Offset < message.Size)
        {
            long before = state.Offset;
            state = await PatchAsync(upload, before, cancellationToken);
            await ProgressAsync(output, oid, state.Offset, state.Offset - before);
        }

        // The part that stores the last byte publishes the object. HEAD finishes that publication
        // when its response was lost or the server could not complete it at the time.
        for (int attempt = 0; !state.Verified; attempt++)
        {
            if (attempt == MaxAttempts) throw new InvalidOperationException("Hosted Git did not confirm the upload");
            await Task.Delay(Backoff(attempt), time, cancellationToken);
            state = await GetStateAsync(upload, cancellationToken);
        }
    }

    private static Uri TransferUri(string href)
    {
        if (!Uri.TryCreate(href, UriKind.Absolute, out Uri? uri)
            || !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException("The LFS upload action is not an HTTPS URL");
        }

        return uri;
    }

    private static async Task<(Uri Upload, bool Parallel)> CreateAsync(
        HttpClient http, Uri creation, Dictionary<string, string>? headers, long size,
        TimeProvider time, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendWithRetryAsync(http, () =>
        {
            HttpRequestMessage request = Request(HttpMethod.Post, creation, headers);
            request.Headers.TryAddWithoutValidation("Upload-Length", size.ToString(CultureInfo.InvariantCulture));
            request.Content = new ByteArrayContent([]);
            return request;
        }, time, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Created || response.Headers.Location is null)
            throw new InvalidOperationException("Invalid tus creation response");
        Uri upload = new(creation, response.Headers.Location);
        // Credentials go only to the upload the creation URL owns.
        if (upload.Scheme != creation.Scheme || upload.Host != creation.Host || upload.Port != creation.Port
            || !string.IsNullOrEmpty(upload.UserInfo) || !string.IsNullOrEmpty(upload.Query)
            || !string.IsNullOrEmpty(upload.Fragment)
            || !upload.AbsolutePath.StartsWith(creation.AbsolutePath.TrimEnd('/') + "/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid tus upload URL");
        }

        bool parallel = response.Headers.TryGetValues(PartSizeHeader, out IEnumerable<string>? sizes)
            && sizes.SingleOrDefault() == PartSize.ToString(CultureInfo.InvariantCulture);
        return (upload, parallel);
    }

    private static async Task<TusState> GetStateAsync(TusUpload upload, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendWithRetryAsync(
            upload.Http, () => Request(HttpMethod.Head, upload.Url, upload.Headers), upload.Time, cancellationToken);
        TusState state = ReadState(response);
        if (ReadNumber(response, "Upload-Length") != upload.Size || state.Offset > upload.Size)
            throw new InvalidOperationException("The tus upload length or offset changed");
        return state;
    }

    /// <summary>Sends the part at <paramref name="offset"/> and returns the state that follows it.</summary>
    private static async Task<TusState> PatchAsync(TusUpload upload, long offset, CancellationToken cancellationToken)
    {
        long length = upload.PartLength(offset);
        for (int attempt = 0; ; attempt++)
        {
            PatchAttempt sent = await SendPartAsync(upload, offset, length, hashState: null, readStoredState: true, cancellationToken);
            if (sent.Stored)
            {
                if (sent.State.Offset != offset + length)
                    throw new InvalidOperationException("Hosted Git accepted a different tus offset");
                return sent.State;
            }

            // The server may have stored the part before its response was lost, even on the
            // last attempt. HEAD tells.
            TusState current = await GetStateAsync(upload, cancellationToken);
            if (current.Offset > offset) return current;
            if (current.Offset < offset) throw new InvalidOperationException("The tus offset moved backwards");
            if (attempt >= MaxAttempts)
                throw new InvalidOperationException("The tus upload did not advance", sent.TransportFailure);
            await Task.Delay(sent.RetryAfter ?? Backoff(attempt), upload.Time, cancellationToken);
        }
    }

    /// <summary>
    /// Sends the part at <paramref name="offset"/> once, naming <paramref name="hashState"/> when the upload is
    /// parallel. A conflict, a lock, a temporary status or a lost response asks for another attempt; any other
    /// rejection fails the upload.
    /// </summary>
    private static async Task<PatchAttempt> SendPartAsync(
        TusUpload upload, long offset, long length, string? hashState, bool readStoredState,
        CancellationToken cancellationToken)
    {
        TimeSpan? retryAfter = null;
        try
        {
            using HttpRequestMessage request = Request(HttpMethod.Patch, upload.Url, upload.Headers);
            request.Headers.TryAddWithoutValidation("Upload-Offset", offset.ToString(CultureInfo.InvariantCulture));
            if (hashState is not null) request.Headers.TryAddWithoutValidation(HashStateHeader, hashState);
            // Each attempt reads its own stream: an early response can leave the transport
            // still sending a previous attempt's body.
            await using Stream file = upload.OpenFile();
            file.Position = offset;
            request.Content = new PartContent(file, length);
            using HttpResponseMessage response = await SendAsync(upload.Http, request, upload.Time, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NoContent)
                return new PatchAttempt(true, readStoredState ? ReadState(response) : default, null, null);

            // Another request still holds this part; the server says when to try again.
            retryAfter = response.Headers.RetryAfter?.Delta;
            if (response.StatusCode is not (HttpStatusCode.Conflict or HttpStatusCode.Locked)
                && !Retryable(response.StatusCode))
            {
                throw new InvalidOperationException(await FailureAsync(response, upload.Time, cancellationToken));
            }
        }
        catch (HttpRequestException ex)
        {
            return new PatchAttempt(false, default, retryAfter, ex);
        }

        return new PatchAttempt(false, default, retryAfter, null);
    }

    /// <summary>
    /// Sends the parts from the accepted offset, <see cref="ConcurrentParts"/> at a time. One reader
    /// hashes the file in order and names the SHA-256 state each part starts from, which hosted Git
    /// checks against the bytes before it; each upload reads its own part again.
    /// </summary>
    private static async Task UploadPartsAsync(
        TusUpload upload, string oid, TusState accepted, TextWriter output, CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var reporting = new SemaphoreSlim(1, 1);
        var parts = Channel.CreateBounded<(long Offset, string Start)>(ConcurrentParts);
        var gate = new object();
        Exception? failure = null;
        long sent = accepted.Offset;
        var stored = new StoredPartFrontier(gate, accepted.Offset, upload.Size);

        async Task RunAsync(Func<Task> work)
        {
            try
            {
                await work();
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // The first failure is the one to report; the others are the cancellation it causes.
                lock (gate) failure ??= ex;
                await stop.CancelAsync();
            }
        }

        async Task HashAsync()
        {
            // A resumed upload continues from the state hosted Git recorded at its offset.
            Sha256ChainingState hash = accepted is { Offset: > 0, HashState: { } recorded }
                ? Sha256ChainingState.Resume(recorded, accepted.Offset)
                : new Sha256ChainingState();
            byte[] buffer = new byte[HashBufferSize];
            await using Stream file = upload.OpenFile();
            file.Position = hash.Length;
            for (long offset = hash.Length; offset < upload.Size; offset += PartSize)
            {
                string start = hash.ToString();
                for (long position = offset, end = Math.Min(upload.Size, offset + PartSize); position < end;)
                {
                    int count = (int)Math.Min(buffer.Length, end - position);
                    if (await file.ReadAtLeastAsync(buffer.AsMemory(0, count), count, throwOnEndOfStream: false, stop.Token) != count)
                        throw new IOException("The LFS file ended before its recorded size");
                    position += count;
                    if (position < upload.Size) hash.Append(buffer.AsSpan(0, count));
                    // Hosted Git would refuse the object at its last part, so that part is never sent.
                    else if (hash.Finish(buffer.AsSpan(0, count)) != oid)
                        throw new InvalidOperationException("The LFS file changed during its upload");
                }

                // A part is sent only once it is hashed, so the last one follows the OID check.
                if (offset >= accepted.Offset) await parts.Writer.WriteAsync((offset, start), stop.Token);
            }

            parts.Writer.Complete();
        }

        async Task SendPartsAsync()
        {
            await foreach ((long offset, string start) in parts.Reader.ReadAllAsync(stop.Token))
            {
                long length = upload.PartLength(offset);
                await PatchPartAsync(upload, offset, start, () => stored.WaitForEarlierPartsAsync(offset, stop.Token), stop.Token);
                stored.MarkStored(offset);

                await reporting.WaitAsync(stop.Token);
                try
                {
                    sent += length;
                    await ProgressAsync(output, oid, sent, length);
                }
                finally
                {
                    reporting.Release();
                }
            }
        }

        await Task.WhenAll([RunAsync(HashAsync), .. Enumerable.Range(0, ConcurrentParts).Select(_ => RunAsync(SendPartsAsync))]);
        cancellationToken.ThrowIfCancellationRequested();
        if (failure is not null) ExceptionDispatchInfo.Throw(failure);
    }

    /// <summary>
    /// Sends one part of a parallel upload, naming the SHA-256 state of the bytes before it. HEAD
    /// shows a stored part only once the parts before it arrived, so after its last resend the part
    /// waits for <paramref name="earlierPartsFinished"/> before HEAD decides.
    /// </summary>
    private static async Task PatchPartAsync(
        TusUpload upload, long offset, string start, Func<Task> earlierPartsFinished, CancellationToken cancellationToken)
    {
        long length = upload.PartLength(offset);
        for (int attempt = 0; ; attempt++)
        {
            PatchAttempt sent = await SendPartAsync(upload, offset, length, start, readStoredState: false, cancellationToken);
            // The accepted offset moves past this part only once every part before it arrived.
            if (sent.Stored) return;

            // Hosted Git replaces a part sent again, so a lost response only costs a resend.
            // A part the accepted offset already passed is stored.
            if ((await GetStateAsync(upload, cancellationToken)).Offset >= offset + length) return;
            if (attempt >= MaxAttempts)
            {
                // One of the lost responses may have stored the part behind an earlier one still in flight.
                await earlierPartsFinished();
                if ((await GetStateAsync(upload, cancellationToken)).Offset >= offset + length) return;
                throw new InvalidOperationException("Hosted Git did not accept an LFS part", sent.TransportFailure);
            }

            await Task.Delay(sent.RetryAfter ?? Backoff(attempt), upload.Time, cancellationToken);
        }
    }

    private static async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpClient http, Func<HttpRequestMessage> createRequest, TimeProvider time, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            using HttpRequestMessage request = createRequest();
            try
            {
                HttpResponseMessage response = await SendAsync(http, request, time, cancellationToken);
                if (response.IsSuccessStatusCode) return response;
                if (attempt >= MaxAttempts || !Retryable(response.StatusCode))
                {
                    using (response) throw new InvalidOperationException(await FailureAsync(response, time, cancellationToken));
                }

                response.Dispose();
            }
            catch (HttpRequestException) when (attempt < MaxAttempts)
            {
                // A transport failure is retried like a temporary server error.
            }

            await Task.Delay(Backoff(attempt), time, cancellationToken);
        }
    }

    /// <summary>Sends a tus request, treating two minutes without request or response progress as a transport failure.</summary>
    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient http, HttpRequestMessage request, TimeProvider time, CancellationToken cancellationToken)
    {
        request.Headers.TryAddWithoutValidation("Tus-Resumable", "1.0.0");
        using var idle = new CancellationTokenSource(IdleTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, idle.Token);
        if (request.Content is PartContent part)
        {
            part.Progress = () =>
            {
                try
                {
                    idle.CancelAfter(IdleTimeout);
                }
                catch (ObjectDisposedException)
                {
                    // The transport can finish writing after the response arrived.
                }
            };
        }

        try
        {
            HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            // Only tus answers carry the version. A proxy's error is left to the status handling,
            // which retries the temporary ones.
            bool tusAnswer = response.IsSuccessStatusCode
                || response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Locked;
            if (tusAnswer && (!response.Headers.TryGetValues("Tus-Resumable", out IEnumerable<string>? versions)
                || versions.SingleOrDefault() != "1.0.0"))
            {
                using (response) throw new InvalidOperationException("Hosted Git did not answer with tus 1.0.0");
            }

            return response;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException("The LFS upload stalled", ex);
        }
    }

    private static HttpRequestMessage Request(HttpMethod method, Uri uri, Dictionary<string, string>? headers)
    {
        var request = new HttpRequestMessage(method, uri);
        foreach ((string name, string value) in headers ?? [])
        {
            // The agent describes its own bodies.
            if (name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)) continue;
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return request;
    }

    private static TusState ReadState(HttpResponseMessage response)
    {
        string? verified = response.Headers.TryGetValues("Upload-Verified", out IEnumerable<string>? values)
            ? values.SingleOrDefault()
            : null;
        string? hashState = response.Headers.TryGetValues(HashStateHeader, out IEnumerable<string>? states)
            ? states.SingleOrDefault()
            : null;
        return new TusState(ReadNumber(response, "Upload-Offset"), verified == "true", hashState);
    }

    private static long ReadNumber(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out IEnumerable<string>? values)
            || !long.TryParse(values.SingleOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out long value))
        {
            throw new InvalidOperationException($"Hosted Git sent no valid {name}");
        }

        return value;
    }

    /// <summary>
    /// Describes a rejection with the start of its body. The body is read for at most a short,
    /// bounded while: a server that stops sending it still leaves the status to report.
    /// </summary>
    private static async Task<string> FailureAsync(
        HttpResponseMessage response, TimeProvider time, CancellationToken cancellationToken)
    {
        string failure = $"Hosted Git rejected the LFS upload: HTTP {(int)response.StatusCode}";
        byte[] buffer = new byte[MaxFailureDetailBytes];
        int length = 0;
        using var timeout = new CancellationTokenSource(FailureDetailTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await using Stream body = await response.Content.ReadAsStreamAsync(linked.Token);
            for (int read; length < buffer.Length
                 && (read = await body.ReadAsync(buffer.AsMemory(length), linked.Token)) > 0;)
            {
                length += read;
            }
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException
            || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // The detail is optional; the status still explains the failure.
        }

        string detail = Encoding.UTF8.GetString(buffer, 0, length).Trim();
        if (detail.Length > 200) detail = detail[..200];
        return detail.Length > 0 ? $"{failure} {detail}" : failure;
    }

    private static bool Retryable(HttpStatusCode status)
        => status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500;

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromSeconds(1 << Math.Min(attempt, 4));

    private static Task ProgressAsync(TextWriter output, string oid, long total, long delta)
        => WriteAsync(output, new { @event = "progress", oid, bytesSoFar = total, bytesSinceLast = delta });

    private static async Task WriteAsync(TextWriter output, object value)
    {
        await output.WriteLineAsync(JsonSerializer.Serialize(value, s_json));
        await output.FlushAsync();
    }

    /// <summary>
    /// The stored prefix of a parallel upload. It locks the gate the upload reports its failure under.
    /// </summary>
    private sealed class StoredPartFrontier(object gate, long start, long size)
    {
        // Every part before this offset is stored; parts that finished out of order wait in `_finished`.
        private long _finishedBefore = start;
        private readonly SortedSet<long> _finished = new();
        private TaskCompletionSource _advanced = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task WaitForEarlierPartsAsync(long offset, CancellationToken cancellationToken)
        {
            while (true)
            {
                Task next;
                lock (gate)
                {
                    if (_finishedBefore >= offset) return;
                    next = _advanced.Task;
                }

                await next.WaitAsync(cancellationToken);
            }
        }

        public void MarkStored(long offset)
        {
            lock (gate)
            {
                _finished.Add(offset);
                while (_finished.Remove(_finishedBefore)) _finishedBefore = Math.Min(size, _finishedBefore + PartSize);
                _advanced.TrySetResult();
                _advanced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }

    /// <summary>One part of the file, streamed from its offset without disposing the file.</summary>
    private sealed class PartContent : HttpContent
    {
        private readonly Stream _file;
        private readonly long _length;

        public PartContent(Stream file, long length)
        {
            _file = file;
            _length = length;
            Headers.ContentType = new MediaTypeHeaderValue("application/offset+octet-stream");
            Headers.ContentLength = length;
        }

        public Action? Progress { get; set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(
            Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[128 * 1024];
            for (long remaining = _length; remaining > 0;)
            {
                int read = await _file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken);
                if (read == 0) throw new IOException("The LFS file ended before its recorded size");
                await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                remaining -= read;
                Progress?.Invoke();
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }
    }
}
