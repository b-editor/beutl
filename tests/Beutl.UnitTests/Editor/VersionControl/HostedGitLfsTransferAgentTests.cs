using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Beutl.Editor.VersionControl;
using Microsoft.Extensions.Time.Testing;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public class HostedGitLfsTransferAgentTests
{
    private readonly List<string> _downloadCacheDirectories = [];

    private string NewDownloadCacheDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"beutl-lfs-test-{Guid.NewGuid():N}");
        _downloadCacheDirectories.Add(directory);
        return directory;
    }

    [TearDown]
    public void CleanupDownloadCaches()
    {
        foreach (string directory in _downloadCacheDirectories)
        {
            if (!Directory.Exists(directory)) continue;
            foreach (string path in Directory.EnumerateFiles(directory)) File.Delete(path);
            Directory.Delete(directory);
        }
        _downloadCacheDirectories.Clear();
    }

    private const long ObjectSize = 5L * 1024 * 1024 * 1024 + 1;
    private const int PartSize = 64 * 1024 * 1024;
    private static readonly string s_oid = new('a', 64);

    [Test]
    public async Task BasicUploadRetriesWithANewFileStreamAndPreservesActionHeaders()
    {
        byte[] bytes = "small upload"u8.ToArray();
        string path = Path.Combine(Path.GetTempPath(), $"beutl-lfs-test-{Guid.NewGuid():N}");
        await File.WriteAllBytesAsync(path, bytes);
        try
        {
            int attempts = 0;
            using var handler = new CallbackHandler(async (request, token) =>
            {
                Assert.That(request.Method, Is.EqualTo(HttpMethod.Put));
                Assert.That(request.Headers.Authorization?.ToString(), Is.EqualTo("Bearer temporary"));
                Assert.That(request.Content!.Headers.ContentLength, Is.EqualTo(bytes.Length));
                Assert.That(request.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/octet-stream"));
                Assert.That(await request.Content.ReadAsByteArrayAsync(token), Is.EqualTo(bytes));
                return new HttpResponseMessage(++attempts == 1
                    ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
            });
            using var http = new HttpClient(handler);
            using var input = TransferInput("upload", bytes.Length, path, "https://storage.example/object",
                new Dictionary<string, string>
                {
                    ["Authorization"] = "Bearer temporary",
                    ["Content-Type"] = "application/octet-stream",
                    ["Content-Length"] = "999",
                    ["x-storage-fixture"] = "value"
                });
            using var output = new StringWriter();
            Assert.That(await HostedGitLfsTransferAgent.RunAsync(input, output, http, CancellationToken.None), Is.Zero);
            Assert.That(attempts, Is.EqualTo(2));
            AssertSuccess(output);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestCase("http://storage.example/object", 4, "Invalid Git LFS transfer action")]
    [TestCase("https://storage.example/object", 5, "file size changed")]
    public async Task RejectsUnsafeOrChangedUploadBeforeMakingARequest(string href, long size, string error)
    {
        using var handler = new CallbackHandler((_, _) => throw new AssertionException("Unexpected HTTP request"));
        using var output = await TransferAsync(handler, "upload", size, href, "data"u8.ToArray());
        AssertError(output, error);
    }

    [Test]
    public async Task ReportsPermanentUploadFailureWithoutRetrying()
    {
        int attempts = 0;
        using var handler = new CallbackHandler((_, _) =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        });
        using var output = await TransferAsync(handler, "upload", 4, "https://storage.example/object", "data"u8.ToArray());
        AssertError(output, "HTTP 403");
        Assert.That(attempts, Is.EqualTo(1));
    }

    [Test]
    public async Task DownloadResumesAtTheReceivedOffsetAndVerifiesTheWholeObject()
    {
        byte[] bytes = "download with an interrupted stream"u8.ToArray();
        string oid = Convert.ToHexStringLower(SHA256.HashData(bytes));
        int requests = 0;
        using var handler = new CallbackHandler((request, _) =>
        {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Get));
            if (++requests == 1)
            {
                Assert.That(request.Headers.Range, Is.Null);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new InterruptedStream(bytes, 7))
                });
            }
            Assert.That(request.Headers.Range!.Ranges.Single().From, Is.EqualTo(7));
            var content = new ByteArrayContent(bytes[7..]);
            content.Headers.ContentRange = new ContentRangeHeaderValue(7, bytes.Length - 1, bytes.Length);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content });
        });
        using var output = await TransferAsync(handler, "download", bytes.Length, "https://storage.example/object", bytes, oid);
        using JsonDocument completed = LastMessage(output);
        AssertSuccess(output);
        string path = completed.RootElement.GetProperty("path").GetString()!;
        try
        {
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(bytes));
            Assert.That(requests, Is.EqualTo(2));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task DownloadKeepsProgressAcrossMoreThanFivePartialResponses()
    {
        byte[] bytes = "eight successive chunks"u8.ToArray();
        string oid = Convert.ToHexStringLower(SHA256.HashData(bytes));
        int requests = 0;
        using var handler = new CallbackHandler((request, _) =>
        {
            int start = (int)(request.Headers.Range?.Ranges.Single().From ?? 0);
            int end = Math.Min(start + 2, bytes.Length);
            requests++;
            var content = new ByteArrayContent(bytes[start..end]);
            content.Headers.ContentRange = new ContentRangeHeaderValue(start, end - 1, bytes.Length);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content });
        });
        using var output = await TransferAsync(handler, "download", bytes.Length, "https://storage.example/object", bytes, oid);
        AssertSuccess(output);
        using JsonDocument completed = LastMessage(output);
        string path = completed.RootElement.GetProperty("path").GetString()!;
        try
        {
            Assert.That(requests, Is.GreaterThan(5));
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(bytes));
        }
        finally { File.Delete(path); }
    }

    [TestCase(2, "exceeds expected size")]
    [TestCase(4, "SHA-256 or size mismatch")]
    public async Task DownloadRejectsWrongSizeOrHash(long size, string error)
    {
        byte[] bytes = "data"u8.ToArray();
        using var handler = new CallbackHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        }));
        using var output = await TransferAsync(handler, "download", size, "https://storage.example/object", bytes);
        AssertError(output, error);
    }

    [TestCase(0)]
    [TestCase(2)]
    public async Task DownloadDoesNotRetryOrReportSuccessAfterADestinationWriteFails(int partiallyWritten)
    {
        byte[] bytes = "data"u8.ToArray();
        string oid = Convert.ToHexStringLower(SHA256.HashData(bytes));
        int requests = 0;
        using var handler = new CallbackHandler((_, _) =>
        {
            requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        });
        using var http = new HttpClient(handler);
        using var input = TransferInput("download", bytes.Length, "fixture", "https://storage.example/object", oid: oid);
        using var output = new StringWriter();
        string? destinationPath = null;
        Assert.That(await HostedGitLfsTransferAgent.RunAsync(input, output, http, CancellationToken.None,
            _ => bytes.Length, _ => new MemoryStream(bytes), path =>
            {
                destinationPath = path;
                return new FailingWriteStream(path, partiallyWritten);
            }, downloadCacheDirectory: NewDownloadCacheDirectory()), Is.Zero);
        AssertError(output, "fixture disk full");
        Assert.That(requests, Is.EqualTo(1));
        Assert.That(File.Exists(destinationPath), Is.False, "failed partial files must be removed");
        Assert.That(output.ToString(), Does.Not.Contain("progress"));
    }

    [Test]
    public async Task DownloadCompletesDespiteProgressBelowTheFormerSpeedThreshold()
    {
        byte[] bytes = new byte[1024];
        string oid = Convert.ToHexStringLower(SHA256.HashData(bytes));
        int requests = 0;
        var clock = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var handler = new CallbackHandler((request, _) =>
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            int start = (int)(request.Headers.Range?.Ranges.Single().From ?? 0);
            requests++;
            var content = new ByteArrayContent(bytes[start..(start + 1)]);
            content.Headers.ContentRange = new ContentRangeHeaderValue(start, start, bytes.Length);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content });
        });
        Task<StringWriter> transfer = TransferAsync(handler, "download", bytes.Length, "https://storage.example/object", bytes,
            oid, clock, cancellation.Token);
        while (!transfer.IsCompleted && !cancellation.IsCancellationRequested)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            await Task.Delay(1);
        }
        using var output = await transfer;
        AssertSuccess(output);
        using JsonDocument completed = LastMessage(output);
        string path = completed.RootElement.GetProperty("path").GetString()!;
        try
        {
            Assert.That(requests, Is.EqualTo(bytes.Length));
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(bytes));
        }
        finally { File.Delete(path); }
    }

    [Test]
    public async Task DownloadWithTinyForwardProgressStillHonorsCallerCancellation()
    {
        int requests = 0;
        string? destinationPath = null;
        var clock = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var handler = new CallbackHandler((request, _) =>
        {
            if (++requests == 5) cancellation.Cancel();
            long start = request.Headers.Range?.Ranges.Single().From ?? 0;
            var content = new ByteArrayContent([42]);
            content.Headers.ContentRange = new ContentRangeHeaderValue(start, start, ObjectSize);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content });
        });
        using var http = new HttpClient(handler);
        using var input = TransferInput("download", ObjectSize, "fixture", "https://storage.example/object");
        using var output = new StringWriter();
        Task<int> transfer = HostedGitLfsTransferAgent.RunAsync(input, output, http, cancellation.Token,
            _ => ObjectSize, _ => new MemoryStream(), path =>
            {
                destinationPath = path;
                return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    128 * 1024, FileOptions.Asynchronous);
            }, clock, NewDownloadCacheDirectory());
        while (!transfer.IsCompleted && !cancellation.IsCancellationRequested)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            await Task.Delay(1);
        }
        Assert.That(await transfer, Is.EqualTo(1));
        Assert.That(requests, Is.EqualTo(5));
        Assert.That(destinationPath, Is.Not.Null);
        Assert.That(File.Exists(destinationPath), Is.True);
        Assert.That(await File.ReadAllBytesAsync(destinationPath!), Is.EqualTo(new byte[] { 42, 42, 42, 42 }));
        using JsonDocument completed = LastMessage(output);
        Assert.That(completed.RootElement.TryGetProperty("error", out _), Is.True);
    }

    [Test]
    public async Task DownloadResumesALargeObjectAcrossManyValidShortRanges()
    {
        byte[] bytes = new byte[16 * 1024 * 1024];
        string oid = Convert.ToHexStringLower(SHA256.HashData(bytes));
        int requests = 0;
        using var handler = new CallbackHandler((request, _) =>
        {
            int start = (int)(request.Headers.Range?.Ranges.Single().From ?? 0);
            int end = Math.Min(start + 128 * 1024, bytes.Length);
            requests++;
            var content = new ByteArrayContent(bytes[start..end]);
            content.Headers.ContentRange = new ContentRangeHeaderValue(start, end - 1, bytes.Length);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content });
        });
        using var output = await TransferAsync(handler, "download", bytes.Length, "https://storage.example/object", bytes, oid);
        AssertSuccess(output);
        using JsonDocument completed = LastMessage(output);
        string path = completed.RootElement.GetProperty("path").GetString()!;
        try
        {
            Assert.That(requests, Is.EqualTo(128));
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(bytes));
        }
        finally { File.Delete(path); }
    }

    [Test]
    public async Task DownloadBoundsResponsesWithoutAnyProgress()
    {
        int requests = 0;
        using var handler = new CallbackHandler((_, _) =>
        {
            requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) });
        });
        using var output = await TransferAsync(handler, "download", ObjectSize, "https://storage.example/object", []);
        AssertError(output, "no progress");
        Assert.That(requests, Is.EqualTo(5));
    }

    [TestCase("headers")]
    [TestCase("body")]
    [TestCase("eof")]
    public async Task DownloadBoundsAnIdleNetworkWait(string stage)
    {
        byte[] bytes = "resume after an idle network wait"u8.ToArray();
        string oid = Convert.ToHexStringLower(SHA256.HashData(bytes));
        string cache = NewDownloadCacheDirectory();
        var clock = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        int requests = 0;
        using var handler = new CallbackHandler(async (request, token) =>
        {
            int attempt = ++requests;
            if (stage == "headers" && attempt == 1) await Task.Delay(Timeout.Infinite, token);
            if (stage != "headers" && attempt == 1)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new IdleReadStream(stage == "body" ? bytes[..5] : bytes))
                };
            int offset = stage == "body" ? 5 : 0;
            Assert.That(request.Headers.Range?.Ranges.Single().From, Is.EqualTo(offset == 0 ? null : (long?)offset));
            var content = new ByteArrayContent(bytes[offset..]);
            if (offset > 0) content.Headers.ContentRange = new ContentRangeHeaderValue(offset, bytes.Length - 1, bytes.Length);
            return new HttpResponseMessage(offset > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = content };
        });
        Task<StringWriter> transfer = TransferAsync(handler, "download", bytes.Length, "https://storage.example/object",
            bytes, oid, clock, cancellation.Token, cache);
        while (!transfer.IsCompleted && !cancellation.IsCancellationRequested)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            await Task.Delay(1);
        }
        using var output = await transfer;
        if (stage == "eof")
        {
            AssertError(output, "stalled");
            Assert.That(requests, Is.EqualTo(1));
        }
        else
        {
            AssertSuccess(output);
            Assert.That(requests, Is.EqualTo(2));
            using JsonDocument completed = LastMessage(output);
            string path = completed.RootElement.GetProperty("path").GetString()!;
            try { Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(bytes)); }
            finally { File.Delete(path); }
        }
        Assert.That(Directory.EnumerateFiles(cache), Is.Empty);
    }

    private sealed class IdleReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position < Length) return await base.ReadAsync(buffer, cancellationToken);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    [TestCase("basic")]
    [TestCase("multipart")]
    [TestCase("tus")]
    public async Task UploadResumesAfterResponseHeadersStall(string kind)
    {
        byte[] bytes = "data"u8.ToArray();
        string oid = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var clock = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        int uploads = 0;
        long offset = 0;
        using var handler = new CallbackHandler(async (request, token) =>
        {
            if (kind == "multipart" && request.Method == HttpMethod.Post)
                return request.RequestUri!.AbsolutePath.EndsWith("/complete", StringComparison.Ordinal)
                    ? JsonResponse(new { oid, size = bytes.Length })
                    : JsonResponse(new { partSize = PartSize, partCount = 1, parts = Array.Empty<object>() });
            if (kind == "tus" && request.Method != HttpMethod.Patch)
            {
                var result = new HttpResponseMessage(request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK);
                result.Headers.TryAddWithoutValidation("Tus-Resumable", "1.0.0");
                if (request.Method == HttpMethod.Post) result.Headers.Location = new Uri(request.RequestUri!.ToString() + "/upload");
                else
                {
                    result.Headers.TryAddWithoutValidation("Upload-Length", bytes.Length.ToString());
                    result.Headers.TryAddWithoutValidation("Upload-Offset", offset.ToString());
                    result.Headers.TryAddWithoutValidation("Upload-Verified", offset == bytes.Length ? "true" : "false");
                }
                return result;
            }
            Assert.That(await request.Content!.ReadAsByteArrayAsync(token), Is.EqualTo(bytes));
            uploads++;
            offset = bytes.Length;
            if (uploads == 1) await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        Task<StringWriter> transfer = TransferAsync(handler, "upload", bytes.Length,
            "https://storage.example/object" + (kind == "basic" ? "" : "/" + kind), bytes, oid, clock, cancellation.Token);
        while (!transfer.IsCompleted && !cancellation.IsCancellationRequested)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            await Task.Delay(1);
        }
        using var output = await transfer;
        AssertSuccess(output);
        Assert.That(uploads, Is.EqualTo(kind == "tus" ? 1 : 2));
    }

    [Test]
    public async Task UploadRetriesWhenTheTransportStopsConsumingTheBody()
    {
        byte[] bytes = "body data"u8.ToArray();
        var clock = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        int attempts = 0;
        using var handler = new CallbackHandler(async (request, token) =>
        {
            if (++attempts == 1) await request.Content!.CopyToAsync(new BlockedWriteStream(), token);
            Assert.That(await request.Content!.ReadAsByteArrayAsync(token), Is.EqualTo(bytes));
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        Task<StringWriter> transfer = TransferAsync(handler, "upload", bytes.Length, "https://storage.example/object",
            bytes, timeProvider: clock, cancellationToken: cancellation.Token);
        while (!transfer.IsCompleted && !cancellation.IsCancellationRequested)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            await Task.Delay(1);
        }
        using var output = await transfer;
        AssertSuccess(output);
        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    public async Task UploadKeepsAdvancingWhenOneRequestExceedsTheIdleWindow()
    {
        byte[] bytes = Enumerable.Range(0, 16).Select(index => (byte)index).ToArray();
        string oid = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var clock = new FakeTimeProvider();
        DateTimeOffset startedAt = clock.GetUtcNow();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        int attempts = 0;
        using var handler = new CallbackHandler(async (request, token) =>
        {
            attempts++;
            Assert.That(await request.Content!.ReadAsByteArrayAsync(token), Is.EqualTo(bytes));
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var http = new HttpClient(handler);
        using var input = TransferInput("upload", bytes.Length, "fixture", "https://storage.example/object", oid: oid);
        using var output = new StringWriter();
        Task<int> transfer = HostedGitLfsTransferAgent.RunAsync(input, output, http, cancellation.Token,
            _ => bytes.Length, _ => new SlowUploadStream(bytes, clock), timeProvider: clock);
        while (!transfer.IsCompleted && !cancellation.IsCancellationRequested)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            await Task.Delay(1);
        }
        Assert.That(await transfer, Is.EqualTo(0));
        AssertSuccess(output);
        Assert.That(attempts, Is.EqualTo(1));
        Assert.That(clock.GetUtcNow() - startedAt, Is.GreaterThan(TimeSpan.FromMinutes(2)));
    }

    private sealed class SlowUploadStream(byte[] bytes, TimeProvider clock) : Stream
    {
        private int _position;
        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override bool CanSeek => false;
        public override long Length => bytes.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position == bytes.Length) return 0;
            await Task.Delay(TimeSpan.FromMinutes(1), clock, cancellationToken);
            buffer.Span[0] = bytes[_position++];
            return 1;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [TestCase("basic")]
    [TestCase("multipart")]
    public async Task UploadKeepsAdvancingWhileTheResponseBodyArrivesSlowly(string kind)
    {
        byte[] bytes = "upload"u8.ToArray();
        string oid = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var clock = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        int uploads = 0;
        int starts = 0;
        using var handler = new CallbackHandler(async (request, token) =>
        {
            if (kind == "multipart" && request.Method == HttpMethod.Post)
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/complete", StringComparison.Ordinal))
                    return JsonResponse(new { oid, size = bytes.Length });
                starts++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new SlowUploadStream(
                    JsonSerializer.SerializeToUtf8Bytes(new { partSize = PartSize, partCount = 1, parts = Array.Empty<object>() }), clock))
                };
            }
            Assert.That(await request.Content!.ReadAsByteArrayAsync(token), Is.EqualTo(bytes));
            uploads++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new SlowUploadStream("accepted"u8.ToArray(), clock)) };
        });
        Task<StringWriter> transfer = TransferAsync(handler, "upload", bytes.Length,
            "https://storage.example/object" + (kind == "basic" ? "" : "/multipart"), bytes, oid, clock, cancellation.Token);
        while (!transfer.IsCompleted && !cancellation.IsCancellationRequested)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            await Task.Delay(10);
        }
        using var output = await transfer;
        AssertSuccess(output);
        Assert.That(uploads, Is.EqualTo(1));
        Assert.That(starts, Is.EqualTo(kind == "basic" ? 0 : 1));
    }

    [Test]
    public async Task UploadRetriesWhenItsResponseBodyStopsAdvancing()
    {
        byte[] bytes = "upload"u8.ToArray();
        var clock = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        int attempts = 0;
        using var handler = new CallbackHandler(async (request, token) =>
        {
            Assert.That(await request.Content!.ReadAsByteArrayAsync(token), Is.EqualTo(bytes));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = ++attempts == 1
                ? new StreamContent(new IdleReadStream("partial response"u8.ToArray())) : new ByteArrayContent([])
            };
        });
        Task<StringWriter> transfer = TransferAsync(handler, "upload", bytes.Length,
            "https://storage.example/object", bytes, timeProvider: clock, cancellationToken: cancellation.Token);
        while (!transfer.IsCompleted && !cancellation.IsCancellationRequested)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            await Task.Delay(10);
        }
        using var output = await transfer;
        AssertSuccess(output);
        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    public async Task DownloadReclaimsOnlyStaleUnownedCompletedHandoffsOnAgentStart()
    {
        string cache = NewDownloadCacheDirectory();
        Directory.CreateDirectory(cache);
        string stale = Path.Combine(cache, $"{s_oid}.{Guid.NewGuid():N}.completed");
        string recent = Path.Combine(cache, $"{s_oid}.{Guid.NewGuid():N}.completed");
        string active = Path.Combine(cache, $"{s_oid}.{Guid.NewGuid():N}.completed");
        foreach (string path in new[] { stale, recent, active }) await File.WriteAllBytesAsync(path, "verified"u8.ToArray());
        foreach (string path in new[] { stale, active }) File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-25));
        using var ownership = new FileStream(active, FileMode.Open, FileAccess.Read, FileShare.None);
        using var input = new StringReader("{\"event\":\"init\",\"operation\":\"download\"}\n{\"event\":\"terminate\"}\n");
        using var output = new StringWriter();
        using var http = new HttpClient();
        Assert.That(await HostedGitLfsTransferAgent.RunAsync(input, output, http, CancellationToken.None,
            _ => 0, _ => Stream.Null, downloadCacheDirectory: cache), Is.Zero);
        Assert.That(File.Exists(stale), Is.False);
        Assert.That(File.Exists(recent), Is.True);
        Assert.That(File.Exists(active), Is.True);
    }

    [Test]
    public async Task DownloadRemovesTheCurrentHandoffWhenItsCompletionMessageFails()
    {
        byte[] bytes = "verified download"u8.ToArray();
        string cache = NewDownloadCacheDirectory();
        string oid = Convert.ToHexStringLower(SHA256.HashData(bytes));
        using var handler = new CallbackHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new ByteArrayContent(bytes) }));
        using var http = new HttpClient(handler);
        using var input = TransferInput("download", bytes.Length, "fixture", "https://storage.example/object", oid: oid);
        using var output = new FailedHandoffWriter();
        await HostedGitLfsTransferAgent.RunAsync(input, output, http, CancellationToken.None,
            _ => 0, _ => Stream.Null, downloadCacheDirectory: cache);
        Assert.That(output.HandoffFailed, Is.True);
        Assert.That(Directory.EnumerateFiles(cache), Is.Empty);
    }

    private sealed class FailedHandoffWriter : StringWriter
    {
        public bool HandoffFailed { get; private set; }
        public override Task WriteLineAsync(string? value)
        {
            if (value?.Contains("\"path\":", StringComparison.Ordinal) == true)
            {
                HandoffFailed = true;
                throw new IOException("Git LFS exited before receiving the path");
            }
            return base.WriteLineAsync(value);
        }
    }

    private sealed class BlockedWriteStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(Task.Delay(Timeout.Infinite, cancellationToken));
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    }

    private sealed class FailingWriteStream(string path, int partiallyWritten) : FileStream(path,
        FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous)
    {
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (partiallyWritten > 0)
                await base.WriteAsync(buffer[..partiallyWritten], cancellationToken);
            throw new IOException("fixture disk full");
        }
    }

    [Test]
    public async Task DownloadRejectsAResumeResponseThatIgnoresTheRange()
    {
        byte[] bytes = "download data"u8.ToArray();
        int requests = 0;
        using var handler = new CallbackHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = ++requests == 1
                ? new StreamContent(new InterruptedStream(bytes, 3))
                : new ByteArrayContent(bytes)
        }));
        using var output = await TransferAsync(handler, "download", bytes.Length, "https://storage.example/object", bytes);
        AssertError(output, "did not honor");
        Assert.That(requests, Is.EqualTo(2));
    }

    [TestCase("{}")]
    [TestCase("{\"event\":\"init\",\"operation\":\"invalid\"}")]
    public async Task InvalidInitiationFailsBeforeTransfer(string init)
    {
        using var handler = new CallbackHandler((_, _) => throw new AssertionException("Unexpected HTTP request"));
        using var http = new HttpClient(handler);
        using var input = new StringReader(init);
        using var output = new StringWriter();
        Assert.That(await HostedGitLfsTransferAgent.RunAsync(input, output, http, CancellationToken.None), Is.EqualTo(1));
        Assert.That(output.ToString(), Does.Contain("Invalid Git LFS initiation"));
    }

    [Test]
    public async Task CompletedMultipartDoesNotReadTheFileOrSendParts()
    {
        using var handler = new CallbackHandler((_, _) => Task.FromResult(JsonResponse(new { complete = true })));
        using var output = await TransferAsync(handler, "upload", 4, "https://beutl.example/multipart", "data"u8.ToArray());
        AssertSuccess(output);
    }

    [TestCase(1, 1)]
    [TestCase(PartSize, 2)]
    public async Task RejectsInvalidMultipartLayout(int partSize, int partCount)
    {
        using var handler = new CallbackHandler((_, _) => Task.FromResult(JsonResponse(new { partSize, partCount })));
        using var output = await TransferAsync(handler, "upload", 4, "https://beutl.example/multipart", "data"u8.ToArray());
        AssertError(output, "part layout");
    }

    [Test]
    public async Task MultipartRejectsCompletionForAnotherObject()
    {
        using var handler = new CallbackHandler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/complete")
            ? JsonResponse(new { oid = new string('b', 64), size = 4 })
            : JsonResponse(new { partSize = PartSize, partCount = 1, parts = new[] { new { partNumber = 1, etag = "accepted", size = 4 } } })));
        using var output = await TransferAsync(handler, "upload", 4, "https://beutl.example/multipart", "data"u8.ToArray());
        AssertError(output, "does not match");
    }

    [Test]
    public async Task MultipartChildPathsPreserveQueryAuthentication()
    {
        var paths = new List<string>();
        using var handler = new CallbackHandler(async (request, token) =>
        {
            Assert.That(request.RequestUri!.Query, Is.EqualTo("?signature=x%2Fy"));
            paths.Add(request.RequestUri.AbsolutePath);
            if (request.Method == HttpMethod.Put)
            {
                Assert.That(await request.Content!.ReadAsByteArrayAsync(token), Is.EqualTo("data"u8.ToArray()));
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return request.RequestUri.AbsolutePath.EndsWith("/complete")
                ? JsonResponse(new { oid = s_oid, size = 4 })
                : JsonResponse(new { partSize = PartSize, partCount = 1, parts = Array.Empty<object>() });
        });
        using var output = await TransferAsync(handler, "upload", 4, "https://storage.example/multipart?signature=x%2Fy", "data"u8.ToArray());
        AssertSuccess(output);
        Assert.That(paths, Is.EqualTo(new[] { "/multipart", "/multipart/parts/1", "/multipart/complete" }));
    }

    [Test]
    public async Task TusRejectsCreationLocationOnAnotherHost()
    {
        using var handler = new CallbackHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Created);
            response.Headers.TryAddWithoutValidation("Tus-Resumable", "1.0.0");
            response.Headers.Location = new Uri("https://elsewhere.example/tus/upload");
            return Task.FromResult(response);
        });
        using var output = await TransferAsync(handler, "upload", 4, "https://beutl.example/tus", "data"u8.ToArray());
        AssertError(output, "Invalid tus upload URL");
    }

    private static StringReader TransferInput(string operation, long size, string path, string href,
        Dictionary<string, string>? header = null, string? oid = null) => new(string.Join('\n',
        JsonSerializer.Serialize(new { @event = "init", operation }),
        JsonSerializer.Serialize(new { @event = operation, oid = oid ?? s_oid, size, path, action = new { href, header } }),
        JsonSerializer.Serialize(new { @event = "terminate" })));

    [TestCase(null)]
    [TestCase("pending")]
    public async Task TusDoesNotCompleteWithoutAnExplicitVerificationStatus(string? verification)
    {
        using var handler = new CallbackHandler((request, _) =>
        {
            var response = new HttpResponseMessage(request.Method == HttpMethod.Post
                ? HttpStatusCode.Created : HttpStatusCode.OK);
            response.Headers.TryAddWithoutValidation("Tus-Resumable", "1.0.0");
            if (request.Method == HttpMethod.Post)
                response.Headers.Location = new Uri("https://beutl.example/tus/upload");
            else
            {
                response.Headers.TryAddWithoutValidation("Upload-Length", "4");
                response.Headers.TryAddWithoutValidation("Upload-Offset", "4");
                if (verification is not null)
                    response.Headers.TryAddWithoutValidation("Upload-Verified", verification);
            }
            return Task.FromResult(response);
        });
        using var output = await TransferAsync(handler, "upload", 4, "https://beutl.example/tus", "data"u8.ToArray());
        AssertError(output, "verification status");
    }

    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.Forbidden)]
    public async Task DownloadRetainsAndRehashesItsPrefixAcrossAuthenticatedAgentInvocations(HttpStatusCode status)
    {
        byte[] bytes = "download across authentication refresh"u8.ToArray();
        string oid = Convert.ToHexStringLower(SHA256.HashData(bytes));
        string cache = NewDownloadCacheDirectory();
        int requests = 0;
        using var expired = new CallbackHandler((request, _) =>
        {
            requests++;
            if (requests == 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes[..5]) });
            Assert.That(request.Headers.Range!.Ranges.Single().From, Is.EqualTo(5));
            return Task.FromResult(new HttpResponseMessage(status));
        });
        using var failed = await TransferAsync(expired, "download", bytes.Length, "https://storage.example/object?token=old",
            bytes, oid, downloadCacheDirectory: cache);
        AssertError(failed, $"HTTP {(int)status}");
        string partial = Path.Combine(cache, oid + ".partial");
        Assert.That(await File.ReadAllBytesAsync(partial), Is.EqualTo(bytes[..5]));
        using var refreshed = new CallbackHandler((request, _) =>
        {
            Assert.That(request.RequestUri!.Query, Is.EqualTo("?token=new"));
            Assert.That(request.Headers.Range!.Ranges.Single().From, Is.EqualTo(5));
            var content = new ByteArrayContent(bytes[5..]);
            content.Headers.ContentRange = new ContentRangeHeaderValue(5, bytes.Length - 1, bytes.Length);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content });
        });
        using var output = await TransferAsync(refreshed, "download", bytes.Length, "https://storage.example/object?token=new",
            bytes, oid, downloadCacheDirectory: cache);
        AssertSuccess(output);
        using JsonDocument completed = LastMessage(output);
        string path = completed.RootElement.GetProperty("path").GetString()!;
        try { Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(bytes)); }
        finally { File.Delete(path); }
        Assert.That(File.Exists(partial), Is.False);
        Assert.That(Directory.EnumerateFiles(cache), Is.Empty);
    }

    [Test]
    public async Task DownloadRejectsAndRemovesACorruptedCachedPrefix()
    {
        byte[] bytes = "data after cached prefix"u8.ToArray();
        string oid = Convert.ToHexStringLower(SHA256.HashData(bytes));
        string cache = NewDownloadCacheDirectory();
        Directory.CreateDirectory(cache);
        string partial = Path.Combine(cache, oid + ".partial");
        await File.WriteAllBytesAsync(partial, "wrong"u8.ToArray());
        using var handler = new CallbackHandler((request, _) =>
        {
            Assert.That(request.Headers.Range!.Ranges.Single().From, Is.EqualTo(5));
            var content = new ByteArrayContent(bytes[5..]);
            content.Headers.ContentRange = new ContentRangeHeaderValue(5, bytes.Length - 1, bytes.Length);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content });
        });
        using var output = await TransferAsync(handler, "download", bytes.Length, "https://storage.example/object", bytes,
            oid, downloadCacheDirectory: cache);
        AssertError(output, "SHA-256 or size mismatch");
        Assert.That(File.Exists(partial), Is.False);
    }

    [Test]
    public async Task DownloadCollectsExpiredPartialDataBeforeStartingAgain()
    {
        byte[] bytes = "new download"u8.ToArray();
        string oid = Convert.ToHexStringLower(SHA256.HashData(bytes));
        string cache = NewDownloadCacheDirectory();
        Directory.CreateDirectory(cache);
        string partial = Path.Combine(cache, oid + ".partial");
        await File.WriteAllBytesAsync(partial, bytes[..5]);
        File.SetLastWriteTimeUtc(partial, DateTime.UtcNow.AddHours(-25));
        string orphanLock = Path.Combine(cache, new string('1', 64) + ".lock");
        await File.WriteAllBytesAsync(orphanLock, []);
        File.SetLastWriteTimeUtc(orphanLock, DateTime.UtcNow.AddHours(-25));
        using var handler = new CallbackHandler((request, _) =>
        {
            Assert.That(request.Headers.Range, Is.Null);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        });
        using var output = await TransferAsync(handler, "download", bytes.Length, "https://storage.example/object", bytes,
            oid, downloadCacheDirectory: cache);
        AssertSuccess(output);
        using JsonDocument completed = LastMessage(output);
        File.Delete(completed.RootElement.GetProperty("path").GetString()!);
        Assert.That(Directory.EnumerateFiles(cache), Is.Empty);
    }

    [Test]
    public async Task AnotherAgentCannotRemoveAnOwnedPartialDownload()
    {
        byte[] bytes = "another agent owns this prefix"u8.ToArray();
        string oid = Convert.ToHexStringLower(SHA256.HashData(bytes));
        string cache = NewDownloadCacheDirectory();
        Directory.CreateDirectory(cache);
        string partial = Path.Combine(cache, oid + ".partial");
        await File.WriteAllBytesAsync(partial, bytes[..5]);
        using var cacheLock = new FileStream(Path.Combine(cache, oid + ".lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var handler = new CallbackHandler((_, _) => throw new AssertionException("Unexpected HTTP request"));
        using var output = await TransferAsync(handler, "download", bytes.Length, "https://storage.example/object", bytes,
            oid, downloadCacheDirectory: cache);
        using JsonDocument completed = LastMessage(output);
        Assert.That(completed.RootElement.TryGetProperty("error", out _), Is.True);
        Assert.That(await File.ReadAllBytesAsync(partial), Is.EqualTo(bytes[..5]));
    }

    [Test]
    public async Task DownloadResumesAfterTransientRetriesAreExhausted()
    {
        byte[] bytes = "resume after repeated server errors"u8.ToArray();
        string oid = Convert.ToHexStringLower(SHA256.HashData(bytes));
        string cache = NewDownloadCacheDirectory();
        var clock = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        int requests = 0;
        using var interrupted = new CallbackHandler((request, _) =>
        {
            if (++requests == 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes[..3]) });
            Assert.That(request.Headers.Range!.Ranges.Single().From, Is.EqualTo(3));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        });
        Task<StringWriter> transfer = TransferAsync(interrupted, "download", bytes.Length, "https://storage.example/object",
            bytes, oid, clock, cancellation.Token, cache);
        while (!transfer.IsCompleted && !cancellation.IsCancellationRequested)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            await Task.Delay(1);
        }
        using var failed = await transfer;
        AssertError(failed, "no progress");
        Assert.That(requests, Is.EqualTo(6));
        Assert.That(await File.ReadAllBytesAsync(Path.Combine(cache, oid + ".partial")), Is.EqualTo(bytes[..3]));
        await CompleteSavedDownloadAsync(bytes, oid, cache, 3);
    }

    [Test]
    public async Task DownloadResumesAfterCallerCancellation()
    {
        byte[] bytes = "resume after cancellation"u8.ToArray();
        string oid = Convert.ToHexStringLower(SHA256.HashData(bytes));
        string cache = NewDownloadCacheDirectory();
        using var cancellation = new CancellationTokenSource();
        int requests = 0;
        using var interrupted = new CallbackHandler((request, token) =>
        {
            if (++requests == 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes[..5]) });
            Assert.That(request.Headers.Range!.Ranges.Single().From, Is.EqualTo(5));
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(token);
        });
        using var http = new HttpClient(interrupted);
        using var input = TransferInput("download", bytes.Length, "fixture", "https://storage.example/object", oid: oid);
        using var output = new StringWriter();
        Assert.That(await HostedGitLfsTransferAgent.RunAsync(input, output, http, cancellation.Token,
            _ => bytes.Length, _ => new MemoryStream(bytes), downloadCacheDirectory: cache), Is.EqualTo(1));
        Assert.That(await File.ReadAllBytesAsync(Path.Combine(cache, oid + ".partial")), Is.EqualTo(bytes[..5]));
        await CompleteSavedDownloadAsync(bytes, oid, cache, 5);
    }

    private async Task CompleteSavedDownloadAsync(byte[] bytes, string oid, string cache, int offset)
    {
        using var refreshed = new CallbackHandler((request, _) =>
        {
            Assert.That(request.Headers.Range!.Ranges.Single().From, Is.EqualTo(offset));
            var content = new ByteArrayContent(bytes[offset..]);
            content.Headers.ContentRange = new ContentRangeHeaderValue(offset, bytes.Length - 1, bytes.Length);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content });
        });
        using var output = await TransferAsync(refreshed, "download", bytes.Length, "https://storage.example/object", bytes,
            oid, downloadCacheDirectory: cache);
        AssertSuccess(output);
        using JsonDocument completed = LastMessage(output);
        string path = completed.RootElement.GetProperty("path").GetString()!;
        try { Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(bytes)); }
        finally { File.Delete(path); }
        Assert.That(Directory.EnumerateFiles(cache), Is.Empty);
    }

    private async Task<StringWriter> TransferAsync(HttpMessageHandler handler, string operation,
        long size, string href, byte[] bytes, string? oid = null, TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default, string? downloadCacheDirectory = null)
    {
        using var http = new HttpClient(handler, disposeHandler: false);
        using var input = TransferInput(operation, size, "fixture", href, oid: oid);
        var output = new StringWriter();
        int result = await HostedGitLfsTransferAgent.RunAsync(input, output, http, cancellationToken,
            _ => bytes.Length, _ => new MemoryStream(bytes, writable: false), timeProvider: timeProvider,
            downloadCacheDirectory: downloadCacheDirectory ?? NewDownloadCacheDirectory());
        Assert.That(result, Is.Zero, output.ToString());
        return output;
    }

    private static JsonDocument LastMessage(StringWriter output) =>
        JsonDocument.Parse(output.ToString().Trim().Split('\n')[^1]);

    private static void AssertSuccess(StringWriter output)
    {
        using JsonDocument message = LastMessage(output);
        Assert.That(message.RootElement.GetProperty("event").GetString(), Is.EqualTo("complete"));
        Assert.That(message.RootElement.TryGetProperty("error", out _), Is.False, output.ToString());
    }

    private static void AssertError(StringWriter output, string expected)
    {
        using JsonDocument message = LastMessage(output);
        Assert.That(message.RootElement.GetProperty("error").GetProperty("message").GetString(), Does.Contain(expected));
    }

    private static HttpResponseMessage JsonResponse(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value))
    };

    private sealed class CallbackHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            callback(request, cancellationToken);
    }

    private sealed class InterruptedStream(byte[] bytes, int interruptAt) : MemoryStream(bytes, writable: false)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position >= interruptAt) throw new IOException("Fixture connection interrupted");
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, interruptAt - (int)Position)], cancellationToken);
        }
    }

    [Test]
    public async Task ResumesAboveFiveGiBAndRetriesAStreamedPart()
    {
        using var handler = new MultipartHandler();
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var input = new StringReader(string.Join("\n",
            JsonSerializer.Serialize(new { @event = "init", operation = "upload" }),
            JsonSerializer.Serialize(new
            {
                @event = "upload",
                oid = s_oid,
                size = ObjectSize,
                path = "virtual-large-media",
                action = new
                {
                    href = $"https://beutl.example/api/v3/git/repo.git/info/lfs/objects/{s_oid}/multipart",
                    header = new Dictionary<string, string>
                    {
                        ["Authorization"] = "Bearer temporary",
                        ["Content-Type"] = "application/octet-stream",
                        ["Content-Language"] = "ja"
                    }
                }
            }),
            JsonSerializer.Serialize(new { @event = "terminate" })));
        using var output = new StringWriter();

        int exitCode = await HostedGitLfsTransferAgent.RunAsync(
            input, output, http, CancellationToken.None,
            static _ => ObjectSize, static _ => new VirtualFile(ObjectSize));

        Assert.That(exitCode, Is.Zero, output.ToString());
        Assert.That(handler.Part80Attempts, Is.EqualTo(2));
        Assert.That(handler.CompletionAttempts, Is.EqualTo(3));
        Assert.That(handler.UploadedParts, Is.EqualTo(new[] { (80, (long)PartSize), (81, 1L) }));
        Assert.That(handler.MaxReadChunk, Is.LessThanOrEqualTo(128 * 1024));
        Assert.That(handler.SawAuthorization, Is.True);
        using JsonDocument last = JsonDocument.Parse(output.ToString().Trim().Split('\n')[^1]);
        Assert.That(last.RootElement.GetProperty("event").GetString(), Is.EqualTo("complete"));
        Assert.That(last.RootElement.GetProperty("oid").GetString(), Is.EqualTo(s_oid));
        Assert.That(last.RootElement.TryGetProperty("error", out _), Is.False);
    }

    [Test]
    public async Task TusResumesAboveFiveGiBAfterAcceptedPatchResponseIsLost()
    {
        using var handler = new TusHandler();
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var input = new StringReader(string.Join("\n",
            JsonSerializer.Serialize(new { @event = "init", operation = "upload" }),
            JsonSerializer.Serialize(new
            {
                @event = "upload",
                oid = s_oid,
                size = ObjectSize,
                path = "virtual-large-media",
                action = new
                {
                    href = $"https://beutl.example/api/v3/git/repo.git/info/lfs/objects/{s_oid}/tus",
                    header = new Dictionary<string, string> { ["Authorization"] = "Bearer temporary" }
                }
            }),
            JsonSerializer.Serialize(new { @event = "terminate" })));
        using var output = new StringWriter();

        int exitCode = await HostedGitLfsTransferAgent.RunAsync(
            input, output, http, CancellationToken.None,
            static _ => ObjectSize, static _ => new VirtualFile(ObjectSize));

        Assert.That(exitCode, Is.Zero, output.ToString());
        Assert.That(handler.UploadedChunks, Is.EqualTo(new[] { (79L * PartSize, (long)PartSize), (80L * PartSize, 1L) }));
        Assert.That(handler.MaxReadChunk, Is.LessThanOrEqualTo(128 * 1024));
        Assert.That(handler.SawAuthorization, Is.True);
        Assert.That(handler.HeadCalls, Is.GreaterThanOrEqualTo(2));
        Assert.That(handler.VerificationPolls, Is.EqualTo(3));
        using JsonDocument last = JsonDocument.Parse(output.ToString().Trim().Split('\n')[^1]);
        Assert.That(last.RootElement.GetProperty("event").GetString(), Is.EqualTo("complete"));
        Assert.That(last.RootElement.TryGetProperty("error", out _), Is.False);
    }

    private sealed class TusHandler : HttpMessageHandler
    {
        private long _offset = 79L * PartSize;
        public int HeadCalls { get; private set; }
        public int VerificationPolls { get; private set; }
        public int MaxReadChunk { get; private set; }
        public bool SawAuthorization { get; private set; }
        public List<(long Offset, long Size)> UploadedChunks { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SawAuthorization |= request.Headers.Authorization?.ToString() == "Bearer temporary";
            Assert.That(request.Headers.GetValues("Tus-Resumable").Single(), Is.EqualTo("1.0.0"));
            if (request.Method == HttpMethod.Post)
            {
                Assert.That(request.Headers.GetValues("Upload-Length").Single(), Is.EqualTo(ObjectSize.ToString()));
                var created = Tus(HttpStatusCode.Created);
                created.Headers.Location = new Uri(request.RequestUri!.AbsoluteUri.TrimEnd('/') + "/11111111-1111-4111-8111-111111111111");
                return created;
            }
            if (request.Method == HttpMethod.Head)
            {
                HeadCalls++;
                var head = Tus(HttpStatusCode.OK);
                head.Headers.TryAddWithoutValidation("Upload-Length", ObjectSize.ToString());
                head.Headers.TryAddWithoutValidation("Upload-Offset", _offset.ToString());
                if (_offset == ObjectSize)
                    head.Headers.TryAddWithoutValidation("Upload-Verified",
                        ++VerificationPolls >= 3 ? "true" : "false");
                return head;
            }
            if (request.Method == HttpMethod.Patch)
            {
                long offset = long.Parse(request.Headers.GetValues("Upload-Offset").Single());
                Assert.That(offset, Is.EqualTo(_offset));
                Assert.That(request.Content!.Headers.ContentType!.MediaType,
                    Is.EqualTo("application/offset+octet-stream"));
                long size = 0;
                byte[] buffer = new byte[128 * 1024];
                await using Stream stream = await request.Content.ReadAsStreamAsync(cancellationToken);
                int read;
                while ((read = await stream.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    MaxReadChunk = Math.Max(MaxReadChunk, read);
                    size += read;
                }
                Assert.That(request.Content.Headers.ContentLength, Is.EqualTo(size));
                UploadedChunks.Add((offset, size));
                _offset += size;
                if (UploadedChunks.Count == 1)
                    return Tus(HttpStatusCode.ServiceUnavailable);
                var patched = Tus(HttpStatusCode.NoContent);
                patched.Headers.TryAddWithoutValidation("Upload-Offset", _offset.ToString());
                return patched;
            }
            throw new InvalidOperationException($"Unexpected tus request: {request.Method} {request.RequestUri}");
        }

        private static HttpResponseMessage Tus(HttpStatusCode status)
        {
            var response = new HttpResponseMessage(status);
            response.Headers.TryAddWithoutValidation("Tus-Resumable", "1.0.0");
            return response;
        }
    }

    private sealed class MultipartHandler : HttpMessageHandler
    {
        public int Part80Attempts { get; private set; }
        public int CompletionAttempts { get; private set; }
        public int MaxReadChunk { get; private set; }
        public bool SawAuthorization { get; private set; }
        public List<(int Number, long Size)> UploadedParts { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SawAuthorization |= request.Headers.Authorization?.ToString() == "Bearer temporary";
            string path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path.EndsWith("/multipart", StringComparison.Ordinal))
            {
                return Json(new
                {
                    complete = false,
                    partSize = PartSize,
                    partCount = 81,
                    parts = Enumerable.Range(1, 79)
                        .Select(number => new { partNumber = number, etag = $"etag-{number}", size = PartSize })
                });
            }
            if (request.Method == HttpMethod.Put && path.Contains("/parts/", StringComparison.Ordinal))
            {
                Assert.That(request.Content!.Headers.ContentType!.MediaType, Is.EqualTo("application/octet-stream"));
                Assert.That(request.Content.Headers.ContentLanguage, Does.Contain("ja"));
                int number = int.Parse(path[(path.LastIndexOf('/') + 1)..]);
                if (number == 80 && ++Part80Attempts == 1)
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

                long size = 0;
                byte[] buffer = new byte[128 * 1024];
                await using Stream stream = await request.Content!.ReadAsStreamAsync(cancellationToken);
                int read;
                while ((read = await stream.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    MaxReadChunk = Math.Max(MaxReadChunk, read);
                    size += read;
                }
                Assert.That(request.Content.Headers.ContentLength, Is.EqualTo(size));
                UploadedParts.Add((number, size));
                return Json(new { partNumber = number, etag = $"etag-{number}", size });
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/complete", StringComparison.Ordinal))
            {
                if (++CompletionAttempts < 3) return Json(new { verifying = true });
                return Json(new { oid = s_oid, size = ObjectSize });
            }
            throw new InvalidOperationException($"Unexpected LFS request: {request.Method} {path}");
        }

        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value))
        };
    }

    private sealed class VirtualFile(long length) : Stream
    {
        private long _position;
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => _position = value; }
        public override long Seek(long offset, SeekOrigin origin) =>
            _position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = (int)Math.Min(count, length - _position);
            Array.Clear(buffer, offset, read);
            _position += read;
            return read;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = (int)Math.Min(buffer.Length, length - _position);
            buffer.Span[..read].Clear();
            _position += read;
            return ValueTask.FromResult(read);
        }
        public override void Flush() => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
