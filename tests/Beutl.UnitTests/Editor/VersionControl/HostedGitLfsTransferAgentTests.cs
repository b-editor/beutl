using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public sealed class HostedGitLfsTransferAgentTests
{
    private const string Creation = "https://beutl.example/api/v3/git/repo.git/info/lfs/objects/oid/tus";
    private const string Credential = "Basic Z2l0OmJndF90b2tlbg==";
    private const long Part = HostedGitLfsTransferAgent.PartSize;

    [Test]
    public async Task Uploads_in_parts_and_completes_when_the_last_part_publishes_the_object()
    {
        var file = new VirtualFile(2 * Part + 3);
        var server = new TusServer(file);
        (int exitCode, List<JsonElement> messages) = await RunAsync(server, file);
        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(server.Patches, Is.EqualTo(new[] { (0L, Part), (Part, Part), (2 * Part, 3L) }));
            Assert.That(server.Published, Is.True);
            Assert.That(server.Authorizations.Distinct(), Is.EqualTo(new[] { Credential }));
            Assert.That(Progress(messages), Is.EqualTo(new[] { Part, 2 * Part, 2 * Part + 3 }));
            Assert.That(Completion(messages).TryGetProperty("error", out _), Is.False);
        });
    }

    [Test]
    public async Task Sends_parts_concurrently_when_hosted_Git_takes_them_out_of_order()
    {
        var file = new VirtualFile(5 * Part + 3);
        var server = new TusServer(file) { Parallel = true, HoldUntilConcurrent = HostedGitLfsTransferAgent.ConcurrentParts };
        (int exitCode, List<JsonElement> messages) = await RunAsync(server, file);
        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(Completion(messages).TryGetProperty("error", out _), Is.False);
            Assert.That(server.Published, Is.True);
            Assert.That(server.MaxConcurrentPatches, Is.EqualTo(HostedGitLfsTransferAgent.ConcurrentParts));
            Assert.That(server.Patches.Order(), Is.EqualTo(Enumerable.Range(0, 6)
                .Select(n => (n * Part, Math.Min(Part, file.Length - n * Part)))));
            Assert.That(Progress(messages), Is.Ordered.And.Length.EqualTo(6));
            Assert.That(Progress(messages)[^1], Is.EqualTo(file.Length));
            // One read hashes the file, and each part is read again for its upload.
            Assert.That(file.BytesRead, Is.EqualTo(2 * file.Length));
        });
    }

    [Test]
    public async Task Resumes_parallel_parts_from_the_state_hosted_Git_recorded()
    {
        var file = new VirtualFile(3 * Part + 5);
        var server = new TusServer(file) { Parallel = true };
        server.Accept(file, 2 * Part);
        long readBefore = file.BytesRead;
        (_, List<JsonElement> messages) = await RunAsync(server, file);
        Assert.Multiple(() =>
        {
            Assert.That(server.Published, Is.True);
            Assert.That(server.Patches.Order(), Is.EqualTo(new[] { (2 * Part, Part), (3 * Part, 5L) }));
            Assert.That(Progress(messages)[0], Is.EqualTo(2 * Part));
            Assert.That(Progress(messages)[^1], Is.EqualTo(file.Length));
            // The accepted bytes are neither hashed nor sent again.
            Assert.That(file.BytesRead - readBefore, Is.EqualTo(2 * (Part + 5)));
        });
    }

    [Test]
    public async Task Sends_a_part_again_after_its_response_was_lost()
    {
        var file = new VirtualFile(2 * Part + 3);
        var server = new TusServer(file) { Parallel = true, LoseResponseAt = Part };
        (_, List<JsonElement> messages) = await RunAsync(server, file);
        Assert.Multiple(() =>
        {
            Assert.That(server.Published, Is.True);
            Assert.That(Completion(messages).TryGetProperty("error", out _), Is.False);
            Assert.That(Progress(messages)[^1], Is.EqualTo(file.Length));
        });
    }

    [Test]
    public async Task Stops_every_part_when_the_file_no_longer_matches_its_object_ID()
    {
        var file = new VirtualFile(Part + 3);
        var held = new TaskCompletionSource();
        var server = new TusServer(new VirtualFile(Part + 3, seed: 7)) { Parallel = true, PartsReleased = held.Task };
        (_, List<JsonElement> messages) = await RunAsync(server, file, oid: new VirtualFile(Part + 3, seed: 7).Oid);
        Assert.Multiple(() =>
        {
            Assert.That(ErrorMessage(messages), Is.EqualTo("The LFS file changed during its upload"));
            Assert.That(server.Published, Is.False);
            Assert.That(server.Patches, Is.Empty);
        });
    }

    [Test]
    public async Task Sends_parts_in_order_when_the_accepted_offset_is_not_a_whole_part()
    {
        var file = new VirtualFile(Part + 7 * 1024 * 1024);
        var server = new TusServer(file) { Parallel = true };
        server.Accept(file, 5 * 1024 * 1024);
        (_, List<JsonElement> messages) = await RunAsync(server, file);
        Assert.Multiple(() =>
        {
            Assert.That(server.Published, Is.True);
            Assert.That(server.NamedStates, Is.Zero);
            Assert.That(server.Patches, Is.EqualTo(new[] { (5L * 1024 * 1024, Part), (5L * 1024 * 1024 + Part, 2L * 1024 * 1024) }));
            Assert.That(Completion(messages).TryGetProperty("error", out _), Is.False);
        });
    }

    [Test]
    public async Task Resumes_an_interrupted_upload_from_the_offset_hosted_Git_reports()
    {
        var file = new VirtualFile(2 * Part + 3);
        var server = new TusServer(file);
        server.Accept(file, Part);
        (_, List<JsonElement> messages) = await RunAsync(server, file);
        Assert.Multiple(() =>
        {
            Assert.That(server.Patches, Is.EqualTo(new[] { (Part, Part), (2 * Part, 3L) }));
            Assert.That(server.Published, Is.True);
            Assert.That(Progress(messages), Is.EqualTo(new[] { Part, 2 * Part, 2 * Part + 3 }));
        });
    }

    [Test]
    public async Task Finishes_after_the_response_to_the_last_part_is_lost()
    {
        var file = new VirtualFile(Part + 5);
        // The server stores the last part but its response never arrives; HEAD publishes it.
        var server = new TusServer(file) { LoseResponseAt = Part };
        (_, List<JsonElement> messages) = await RunAsync(server, file);
        Assert.Multiple(() =>
        {
            Assert.That(server.Patches, Is.EqualTo(new[] { (0L, Part), (Part, 5L) }));
            Assert.That(server.Published, Is.True);
            Assert.That(Completion(messages).TryGetProperty("error", out _), Is.False);
        });
    }

    [Test]
    public async Task Waits_while_another_request_still_holds_the_part()
    {
        var file = new VirtualFile(9);
        var server = new TusServer(file) { LockedResponses = 2 };
        (_, List<JsonElement> messages) = await RunAsync(server, file);
        Assert.Multiple(() =>
        {
            Assert.That(server.Patches, Is.EqualTo(new[] { (0L, 9L) }));
            // The size check, then each attempt reads its own stream in case the transport still sends an earlier one.
            Assert.That(file.Opened, Is.EqualTo(4));
            Assert.That(server.Published, Is.True);
            Assert.That(Completion(messages).TryGetProperty("error", out _), Is.False);
        });
    }

    [Test]
    public async Task Reports_content_that_does_not_match_the_object_without_retrying_it()
    {
        var file = new VirtualFile(9);
        var server = new TusServer(new VirtualFile(9, seed: 7));
        (_, List<JsonElement> messages) = await RunAsync(server, file);
        Assert.Multiple(() =>
        {
            Assert.That(server.Patches, Has.Count.EqualTo(1));
            Assert.That(server.Published, Is.False);
            Assert.That(ErrorMessage(messages), Does.Contain("HTTP 422 LFS SHA-256 mismatch"));
        });
    }

    [Test]
    public async Task Finishes_an_empty_object_when_it_is_created()
    {
        var file = new VirtualFile(0);
        var server = new TusServer(file);
        (_, List<JsonElement> messages) = await RunAsync(server, file);
        Assert.Multiple(() =>
        {
            Assert.That(server.Patches, Is.Empty);
            Assert.That(server.Published, Is.True);
            Assert.That(Completion(messages).TryGetProperty("error", out _), Is.False);
        });
    }

    [Test]
    public async Task Finds_the_stored_part_when_the_last_attempt_loses_its_response()
    {
        var file = new VirtualFile(9);
        // Five unavailable answers use every retry; the last attempt is stored but its response is lost.
        var server = new TusServer(file) { UnavailablePatches = 5, LoseResponseAt = 0 };
        (_, List<JsonElement> messages) = await RunAsync(server, file);
        Assert.Multiple(() =>
        {
            Assert.That(server.Published, Is.True);
            Assert.That(Completion(messages).TryGetProperty("error", out _), Is.False);
        });
    }

    [Test]
    public async Task Retries_a_temporary_proxy_error_that_carries_no_tus_header()
    {
        var file = new VirtualFile(9);
        var server = new TusServer(file) { ProxyErrors = 2 };
        (_, List<JsonElement> messages) = await RunAsync(server, file);
        Assert.Multiple(() =>
        {
            Assert.That(server.Published, Is.True);
            Assert.That(Completion(messages).TryGetProperty("error", out _), Is.False);
        });
    }

    [Test]
    public async Task Reports_the_status_when_an_error_body_stalls_or_never_ends()
    {
        var stalled = new TusServer(new VirtualFile(9, seed: 7)) { MismatchBody = () => new StalledStream() };
        var endless = new TusServer(new VirtualFile(9, seed: 7)) { MismatchBody = () => new EndlessStream() };
        var file = new VirtualFile(9);
        (_, List<JsonElement> stalledMessages) = await RunAsync(stalled, file, time: new InstantDelays(expireFailureDetails: true));
        (_, List<JsonElement> endlessMessages) = await RunAsync(endless, file);
        Assert.Multiple(() =>
        {
            Assert.That(ErrorMessage(stalledMessages), Is.EqualTo("Hosted Git rejected the LFS upload: HTTP 422"));
            Assert.That(ErrorMessage(endlessMessages), Is.EqualTo($"Hosted Git rejected the LFS upload: HTTP 422 {new string('x', 200)}"));
        });
    }

    [Test]
    public async Task Never_follows_a_redirect_with_the_actions_credentials()
    {
        using SocketsHttpHandler handler = HostedGitLfsTransferAgent.CreateHandler();
        var file = new VirtualFile(9);
        var server = new TusServer(file) { RedirectCreation = true };
        (_, List<JsonElement> messages) = await RunAsync(server, file);
        Assert.Multiple(() =>
        {
            Assert.That(handler.AllowAutoRedirect, Is.False);
            Assert.That(ErrorMessage(messages), Is.EqualTo("Hosted Git rejected the LFS upload: HTTP 307"));
            Assert.That(server.Requests, Is.EqualTo(1));
        });
    }

    // Credentials go only to an upload under the creation URL on the same origin.
    [TestCase("https://elsewhere.example/upload")]
    [TestCase("http://beutl.example/api/v3/git/repo.git/info/lfs/objects/oid/tus/id")]
    [TestCase("https://beutl.example:8443/api/v3/git/repo.git/info/lfs/objects/oid/tus/id")]
    [TestCase("https://u:p@beutl.example/api/v3/git/repo.git/info/lfs/objects/oid/tus/id")]
    [TestCase(Creation + "/id?x=1")]
    [TestCase(Creation + "/id#f")]
    [TestCase("https://beutl.example/api/v3/git/other/tus/id")]
    [TestCase(Creation + "x/id")]
    public async Task Rejects_a_creation_location_outside_the_creation_url(string location)
    {
        var file = new VirtualFile(9);
        var server = new TusServer(file) { Location = location };
        (_, List<JsonElement> messages) = await RunAsync(server, file);
        Assert.Multiple(() =>
        {
            Assert.That(ErrorMessage(messages), Does.Contain("Invalid tus upload URL"));
            Assert.That(server.Requests, Is.EqualTo(1));
        });
    }

    [TestCase("http://beutl.example/tus", true)]
    [TestCase("https://git:secret@beutl.example/tus", true)]
    [TestCase("http://127.0.0.1:8787/tus", false)]
    public async Task Sends_credentials_only_over_HTTPS_except_to_this_machine(string href, bool rejected)
    {
        var file = new VirtualFile(9);
        var server = new TusServer(file) { Location = $"{href}/1b4a4c4e-7d6b-4f1e-9f43-0a1b2c3d4e5f" };
        (_, List<JsonElement> messages) = await RunAsync(server, file, href);
        Assert.That(Completion(messages).TryGetProperty("error", out _), Is.EqualTo(rejected));
        if (rejected) Assert.That(server.Requests, Is.Zero);
    }

    [Test]
    public async Task Reports_a_file_whose_size_changed_before_any_request()
    {
        var file = new VirtualFile(9);
        var server = new TusServer(file);
        (_, List<JsonElement> messages) = await RunAsync(server, file, size: 10);
        Assert.Multiple(() =>
        {
            Assert.That(ErrorMessage(messages), Does.Contain("changed size"));
            Assert.That(server.Requests, Is.Zero);
        });
    }

    [TestCase("{\"event\":\"init\",\"operation\":\"download\"}")]
    [TestCase("{}")]
    [TestCase("not json")]
    public async Task Refuses_anything_but_an_upload_session(string init)
    {
        var server = new TusServer(new VirtualFile(0));
        using var http = new HttpClient(server);
        using var output = new StringWriter();
        int exitCode = await HostedGitLfsTransferAgent.RunAsync(
            new StringReader(init), output, http, _ => throw new AssertionException("No file is read"),
            new InstantDelays(), CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(output.ToString(), Does.Contain("uploads only"));
            Assert.That(server.Requests, Is.Zero);
        });
    }

    [Test]
    public void Offers_the_agent_for_uploads_through_process_scoped_Git_configuration()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HostedGitLfsTransferAgent.GitConfigArguments("/Applications/Beutl.app/Contents/MacOS/Beutl", null), Is.EqualTo(new[]
            {
                "-c", "lfs.customtransfer.beutl-tus.path=/Applications/Beutl.app/Contents/MacOS/Beutl",
                "-c", "lfs.customtransfer.beutl-tus.args=--git-lfs-transfer",
                "-c", "lfs.customtransfer.beutl-tus.direction=upload",
            }));
            // A development run through the dotnet host passes the application assembly, quoted for the shell.
            Assert.That(HostedGitLfsTransferAgent.GitConfigArguments("/usr/local/share/dotnet/dotnet", "/src/it's here/Beutl.dll")[3],
                Is.EqualTo(@"lfs.customtransfer.beutl-tus.args='/src/it'\''s here/Beutl.dll' --git-lfs-transfer"));
            Assert.That(HostedGitLfsTransferAgent.GitConfigArguments(null, null), Is.Empty);
        });
    }

    private static async Task<(int ExitCode, List<JsonElement> Messages)> RunAsync(
        TusServer server, VirtualFile file, string href = Creation, long? size = null, TimeProvider? time = null, string? oid = null)
    {
        using var http = new HttpClient(server);
        using var output = new StringWriter();
        string input = string.Join('\n',
            JsonSerializer.Serialize(new { @event = "init", operation = "upload", remote = "origin", concurrent = true, concurrenttransfers = 8 }),
            JsonSerializer.Serialize(new
            {
                @event = "upload",
                oid = oid ?? file.Oid,
                size = size ?? file.Length,
                path = "media.bin",
                action = new { href, header = new Dictionary<string, string> { ["Authorization"] = Credential } },
            }),
            JsonSerializer.Serialize(new { @event = "terminate" }));
        int exitCode = await HostedGitLfsTransferAgent.RunAsync(
            new StringReader(input), output, http, _ => file.Open(), time ?? new InstantDelays(), CancellationToken.None);
        List<JsonElement> messages = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement).ToList();
        Assert.That(messages[0].EnumerateObject(), Is.Empty, "the agent confirms initiation with an empty object");
        return (exitCode, messages);
    }

    private static long[] Progress(List<JsonElement> messages) => messages
        .Where(message => message.TryGetProperty("event", out JsonElement e) && e.GetString() == "progress")
        .Select(message => message.GetProperty("bytesSoFar").GetInt64()).ToArray();

    private static JsonElement Completion(List<JsonElement> messages) => messages
        .Single(message => message.TryGetProperty("event", out JsonElement e) && e.GetString() == "complete");

    private static string ErrorMessage(List<JsonElement> messages)
        => Completion(messages).GetProperty("error").GetProperty("message").GetString()!;

    /// <summary>Hosted Git's tus endpoint: one upload per object, published by the part that ends it.</summary>
    private sealed class TusServer(VirtualFile expected) : HttpMessageHandler
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        // Parallel parts: the chaining state of the accepted bytes, and parts that arrived early.
        private readonly Sha256ChainingState _chain = new();
        private readonly Dictionary<long, (string Start, byte[] Bytes)> _waiting = [];
        private readonly SemaphoreSlim _lock = new(1, 1);
        private int _requests;
        private int _inFlight;
        private long _offset;
        private long? _length;

        /// <summary>Takes parts out of order, like hosted Git, when they name their SHA-256 state.</summary>
        public bool Parallel { get; init; }
        /// <summary>Holds every parallel part until the test lets it through.</summary>
        public Task PartsReleased { get; init; } = Task.CompletedTask;
        /// <summary>
        /// Holds parallel parts until this many are in flight. In memory a part arrives faster than
        /// the agent hashes the next one, which a real connection never does.
        /// </summary>
        public int HoldUntilConcurrent { get; init; } = 1;
        private readonly TaskCompletionSource _concurrent = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int MaxConcurrentPatches { get; private set; }
        public int NamedStates { get; private set; }

        public string Location { get; init; } = Creation + "/1b4a4c4e-7d6b-4f1e-9f43-0a1b2c3d4e5f";
        public long? LoseResponseAt { get; set; }
        public int LockedResponses { get; set; }
        public int UnavailablePatches { get; set; }
        public int ProxyErrors { get; set; }
        public bool RedirectCreation { get; init; }
        public Func<Stream> MismatchBody { get; init; } = () => new MemoryStream("LFS SHA-256 mismatch"u8.ToArray());
        public int Requests => _requests;
        public bool Published { get; private set; }
        public List<(long Offset, long Length)> Patches { get; } = [];
        public List<string> Authorizations { get; } = [];

        public void Accept(VirtualFile file, long length)
        {
            _length = file.Length;
            using Stream stream = file.Open();
            Append(stream, length);
            if (!Parallel) return;
            using Stream again = file.Open();
            byte[] bytes = new byte[length];
            again.ReadExactly(bytes);
            _chain.Append(bytes);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            lock (Authorizations) Authorizations.Add(request.Headers.Authorization?.ToString() ?? "");
            Assert.That(request.Headers.GetValues("Tus-Resumable").Single(), Is.EqualTo("1.0.0"));
            if (ProxyErrors > 0)
            {
                // A proxy in front of hosted Git answers without tus headers.
                ProxyErrors--;
                return new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>bad gateway</html>") };
            }

            if (request.Method == HttpMethod.Post && RedirectCreation)
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
                redirect.Headers.Location = new Uri("https://elsewhere.example/tus");
                return redirect;
            }

            if (request.Method == HttpMethod.Post)
            {
                _length ??= long.Parse(request.Headers.GetValues("Upload-Length").Single());
                if (_length == 0) Publish();
                HttpResponseMessage created = Tus(HttpStatusCode.Created);
                created.Headers.Location = new Uri(Location);
                if (Parallel) created.Headers.Add("Beutl-Part-Size", Part.ToString());
                return created;
            }

            Assert.That(request.RequestUri!.ToString(), Is.EqualTo(Location));
            if (request.Method == HttpMethod.Head)
            {
                await _lock.WaitAsync(cancellationToken);
                try
                {
                    // HEAD finishes a publication whose response was lost.
                    if (_offset == _length && !Published) Publish();
                    return State(HttpStatusCode.OK);
                }
                finally
                {
                    _lock.Release();
                }
            }

            Assert.That(request.Method, Is.EqualTo(HttpMethod.Patch));
            if (LockedResponses > 0)
            {
                LockedResponses--;
                HttpResponseMessage locked = Tus(HttpStatusCode.Locked);
                locked.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
                return locked;
            }

            if (UnavailablePatches > 0)
            {
                UnavailablePatches--;
                return Tus(HttpStatusCode.ServiceUnavailable);
            }

            long offset = long.Parse(request.Headers.GetValues("Upload-Offset").Single());
            long length = request.Content!.Headers.ContentLength!.Value;
            Assert.That(request.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/offset+octet-stream"));
            if (request.Headers.TryGetValues("Beutl-Sha256-State", out IEnumerable<string>? starts))
                return await PatchPartAsync(request, offset, length, starts.Single(), cancellationToken);
            if (offset != _offset) return State(HttpStatusCode.Conflict);
            Patches.Add((offset, length));
            Append(await request.Content.ReadAsStreamAsync(cancellationToken), length);
            if (_offset == _length)
            {
                if (!Convert.ToHexStringLower(_hash.GetCurrentHash()).Equals(expected.Oid, StringComparison.Ordinal))
                {
                    HttpResponseMessage mismatch = Tus(HttpStatusCode.UnprocessableEntity);
                    mismatch.Content = new StreamContent(MismatchBody());
                    return mismatch;
                }
                if (LoseResponseAt == offset) throw new HttpRequestException("connection reset");
                Publish();
            }

            return State(HttpStatusCode.NoContent);
        }

        /// <summary>Stores a part that names its starting state; the accepted offset passes it once the state matches.</summary>
        private async Task<HttpResponseMessage> PatchPartAsync(
            HttpRequestMessage request, long offset, long length, string start, CancellationToken cancellationToken)
        {
            Assert.That(Parallel, "the agent names states only to a server that takes parts out of order");
            int inFlight = Interlocked.Increment(ref _inFlight);
            try
            {
                lock (_waiting) MaxConcurrentPatches = Math.Max(MaxConcurrentPatches, inFlight);
                if (inFlight >= HoldUntilConcurrent) _concurrent.TrySetResult();
                await _concurrent.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                byte[] bytes = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                // Real transfers take a while, so the other parts' requests overlap this one.
                await Task.Delay(20, cancellationToken);
                await PartsReleased.WaitAsync(cancellationToken);
                await _lock.WaitAsync(cancellationToken);
                try
                {
                    NamedStates++;
                    Assert.That(offset % Part, Is.Zero);
                    Assert.That(bytes.LongLength, Is.EqualTo(Math.Min(Part, _length!.Value - offset)).And.EqualTo(length));
                    if (offset < _offset) return State(HttpStatusCode.Conflict);
                    Patches.Add((offset, length));
                    _waiting[offset] = (start, bytes);
                    while (_waiting.Remove(_offset, out (string Start, byte[] Bytes) next))
                    {
                        if (next.Start != _chain.ToString()) return Mismatch();
                        if (_offset + next.Bytes.Length < _length)
                        {
                            _chain.Append(next.Bytes);
                            _offset += next.Bytes.Length;
                            continue;
                        }

                        if (_chain.Finish(next.Bytes) != expected.Oid) return Mismatch();
                        _offset += next.Bytes.Length;
                        Publish();
                    }

                    if (LoseResponseAt == offset)
                    {
                        LoseResponseAt = null;
                        throw new HttpRequestException("connection reset");
                    }

                    return State(HttpStatusCode.NoContent);
                }
                finally
                {
                    _lock.Release();
                }
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        private HttpResponseMessage Mismatch()
        {
            HttpResponseMessage mismatch = Tus(HttpStatusCode.UnprocessableEntity);
            mismatch.Content = new StreamContent(MismatchBody());
            return mismatch;
        }

        private void Append(Stream stream, long length)
        {
            byte[] buffer = new byte[1 << 20];
            for (long remaining = length; remaining > 0;)
            {
                int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                Assert.That(read, Is.Positive);
                _hash.AppendData(buffer, 0, read);
                remaining -= read;
            }

            _offset += length;
        }

        private void Publish() => Published = true;

        private HttpResponseMessage State(HttpStatusCode status)
        {
            HttpResponseMessage response = Tus(status);
            response.Headers.Add("Upload-Offset", _offset.ToString());
            response.Headers.Add("Upload-Length", _length.ToString());
            response.Headers.Add("Upload-Verified", Published ? "true" : "false");
            if (Parallel && _offset > 0 && !Published) response.Headers.Add("Beutl-Sha256-State", _chain.ToString());
            return response;
        }

        private static HttpResponseMessage Tus(HttpStatusCode status)
        {
            var response = new HttpResponseMessage(status);
            response.Headers.Add("Tus-Resumable", "1.0.0");
            return response;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hash.Dispose();
                _lock.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>A response body whose sender stopped: reads wait until they are cancelled.</summary>
    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A response body that never ends.</summary>
    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            buffer.AsSpan(offset, count).Fill((byte)'x');
            return count;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Deterministic file content of any length, generated rather than stored.</summary>
    private sealed class VirtualFile(long length, int seed = 0)
    {
        private int _opened;
        private long _bytesRead;

        public long Length => length;

        public string Oid { get; } = Hash(length, seed);

        public int Opened => _opened;

        public long BytesRead => Interlocked.Read(ref _bytesRead);

        public Stream Open()
        {
            Interlocked.Increment(ref _opened);
            return new Content(length, seed, read => Interlocked.Add(ref _bytesRead, read));
        }

        private static string Hash(long length, int seed)
        {
            using var stream = new Content(length, seed, _ => { });
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }

        private sealed class Content(long length, int seed, Action<int> counted) : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => length;
            public override long Position { get; set; }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int read = (int)Math.Max(0, Math.Min(count, length - Position));
                for (int i = 0; i < read; i++) buffer[offset + i] = (byte)((Position + i) * 31 + seed);
                Position += read;
                counted(read);
                return read;
            }

            public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => Position + offset,
                _ => length + offset,
            };

            public override void Flush() { }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Runs retry delays at once. The two-minute idle timeout never elapses, and the time allowed
    /// for an error body elapses only when a test asks for it.
    /// </summary>
    private sealed class InstantDelays(bool expireFailureDetails = false) : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            bool expires = dueTime == HostedGitLfsTransferAgent.FailureDetailTimeout
                ? expireFailureDetails
                : dueTime < HostedGitLfsTransferAgent.IdleTimeout;
            if (expires) ThreadPool.QueueUserWorkItem(_ => callback(state));
            return new Timer();
        }

        private sealed class Timer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
