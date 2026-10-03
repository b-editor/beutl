using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public class HostedGitLfsTransferAgentTests
{
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
            }), Is.Zero);
        AssertError(output, "fixture disk full");
        Assert.That(requests, Is.EqualTo(1));
        Assert.That(File.Exists(destinationPath), Is.False, "failed partial files must be removed");
        Assert.That(output.ToString(), Does.Not.Contain("progress"));
    }

    [Test]
    public async Task DownloadBoundsRepeatedOneByteResponsesForALargeObject()
    {
        int requests = 0;
        var clock = new AdvancingTimeProvider();
        using var handler = new CallbackHandler((request, _) =>
        {
            clock.Advance(TimeSpan.FromSeconds(15));
            long start = request.Headers.Range?.Ranges.Single().From ?? 0;
            requests++;
            var content = new ByteArrayContent([42]);
            content.Headers.ContentRange = new ContentRangeHeaderValue(start, start, ObjectSize);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content });
        });
        using var output = await TransferAsync(handler, "download", ObjectSize, "https://storage.example/object", [], timeProvider: clock);
        AssertError(output, "insufficient progress");
        Assert.That(requests, Is.EqualTo(4));
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

    private sealed class AdvancingTimeProvider : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan duration) => _timestamp += (long)duration.TotalMilliseconds;
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

    private static async Task<StringWriter> TransferAsync(HttpMessageHandler handler, string operation,
        long size, string href, byte[] bytes, string? oid = null, TimeProvider? timeProvider = null)
    {
        using var http = new HttpClient(handler, disposeHandler: false);
        using var input = TransferInput(operation, size, "fixture", href, oid: oid);
        var output = new StringWriter();
        int result = await HostedGitLfsTransferAgent.RunAsync(input, output, http, CancellationToken.None,
            _ => bytes.Length, _ => new MemoryStream(bytes, writable: false), timeProvider: timeProvider);
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
