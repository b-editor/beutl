using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Api;

[TestFixture]
public sealed class HostedGitRepositoryServiceTests
{
    internal const string RepositoryId = "a3a8927c-bcf4-4f6e-91f4-64897320378e";
    internal const string TokenId = "0139a302-0f90-402c-a494-84786a29c572";
    internal const string AccessToken = "example-write-token";
    private static DateTimeOffset Now { get; } = DateTimeOffset.Parse("2026-11-24T07:00:00Z");

    internal static GitRepositorySummary Repository(string name = "Sample")
        => new(RepositoryId, name, $"{BeutlApiApplication.BaseUrl}/api/v3/git/{RepositoryId}.git", Now, Now);

    internal static CreatedGitAccessToken Token(string scope = "write")
        => new() { Id = TokenId, Name = "Beutl Desktop", Scope = scope, Hint = "…oken", CreatedAt = Now, Token = AccessToken };

    internal static BeutlApiApplication CreateApp(HttpClient http)
        => new(http, new ExtensionProvider(new ExtensionConfig()), new ViewConfig(), new UpdateConfig());

    internal static HttpResponseMessage Json(object data, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = JsonContent.Create(data, data.GetType()) };

    [Test]
    public async Task Maps_an_HTTP_timeout_to_unavailable()
    {
        using var http = new HttpClient(new DelegateHttpHandler(_ =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("HTTP request timed out", new TimeoutException()))));
        await using var app = CreateApp(http);
        ApiTestAuthentication.SignIn(app);
        var service = app.GetResource<HostedGitRepositoryService>();

        var failure = await Assert.ThrowsAsync<HostedGitApiException>(() => service.ListAsync());
        Assert.That(failure!.Error, Is.EqualTo(HostedGitError.Unavailable));
    }

    [Test]
    public async Task Endpoints_use_authenticated_methods_and_the_web_request_contract()
    {
        Guid id = Guid.Parse(RepositoryId);
        var creationId = Guid.NewGuid();
        var observed = new List<string>();
        using var http = new HttpClient(new DelegateHttpHandler(async request =>
        {
            Assert.That(request.Headers.Authorization?.ToString(), Is.EqualTo("Bearer desktop-token"));
            string path = request.RequestUri!.AbsolutePath;
            observed.Add($"{request.Method} {path}");
            if (request.Method == HttpMethod.Delete)
                return new HttpResponseMessage(HttpStatusCode.NoContent);

            if (request.Method is { } method && (method == HttpMethod.Post || method == HttpMethod.Patch))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                var fields = body.RootElement;
                if (path.EndsWith("/tokens", StringComparison.Ordinal))
                {
                    Assert.That(fields.GetProperty("name").GetString(), Is.EqualTo("Backup"));
                    Assert.That(fields.GetProperty("scope").GetString(), Is.EqualTo("read"));
                    return Json(Token("read"), HttpStatusCode.Created);
                }
                if (method == HttpMethod.Post)
                {
                    Assert.That(fields.GetProperty("creationId").GetString(), Is.EqualTo(creationId.ToString("D")));
                    Assert.That(fields.GetProperty("ownerId").GetString(), Is.EqualTo("owner"));
                    Assert.That(fields.GetProperty("name").GetString(), Is.EqualTo("Sample"));
                    return Json(Repository(), HttpStatusCode.Created);
                }

                Assert.That(fields.GetProperty("name").GetString(), Is.EqualTo("Renamed"));
                return Json(Repository("Renamed"));
            }

            if (path.EndsWith("/tokens", StringComparison.Ordinal))
                return Json(new GitAccessTokenListResponse([new GitAccessTokenSummary
                {
                    Id = TokenId, Name = "Backup", Scope = "read", Hint = "…oken", CreatedAt = Now, LastUsedAt = Now,
                }]));
            return path == "/api/v3/repos"
                ? Json(new GitRepositoryListResponse([Repository()]))
                : Json(Repository());
        }));
        await using var app = CreateApp(http);
        AuthenticatedUser owner = ApiTestAuthentication.SignIn(app, id: "owner", token: "desktop-token");
        var service = app.GetResource<HostedGitRepositoryService>();

        Assert.That(await service.ListAsync(), Is.EqualTo(new[] { Repository() }));
        Assert.That(await service.CreateAsync(" Sample ", creationId, owner), Is.EqualTo(Repository()));
        Assert.That(await service.GetAsync(id), Is.EqualTo(Repository()));
        Assert.That(await service.RenameAsync(id, "Renamed"), Is.EqualTo(Repository("Renamed")));
        Assert.That((await service.ListTokensAsync(id)).Single().LastUsedAt, Is.EqualTo(Now));
        var token = await service.CreateTokenAsync(id, new CreateGitAccessTokenRequest(" Backup ", "read"));
        Assert.That(token.Token, Is.EqualTo(AccessToken));
        Assert.That(token.ToString(), Does.Not.Contain(AccessToken));
        await service.RevokeTokenAsync(id, Guid.Parse(TokenId));
        await service.DeleteAsync(id);
        Assert.That(observed, Is.EqualTo(new[]
        {
            "GET /api/v3/repos", "POST /api/v3/repos", $"GET /api/v3/repos/{RepositoryId}",
            $"PATCH /api/v3/repos/{RepositoryId}", $"GET /api/v3/repos/{RepositoryId}/tokens",
            $"POST /api/v3/repos/{RepositoryId}/tokens", $"DELETE /api/v3/repos/{RepositoryId}/tokens/{TokenId}",
            $"DELETE /api/v3/repos/{RepositoryId}",
        }));
    }

    [Test]
    public async Task Repositories_use_the_native_device_proof()
    {
        var auth = new AuthResponse
        {
            Token = "issued-access-token",
            RefreshToken = "refresh-token",
            Expiration = DateTime.UtcNow.AddHours(1),
            DpopPrivateKey = NativeProofKey.Create(),
        };
        using var http = new HttpClient(new DelegateHttpHandler(request =>
        {
            Assert.That(request.Headers.Authorization?.ToString(), Is.EqualTo("DPoP issued-access-token"));
            Assert.That(request.Headers.GetValues("DPoP").Single().Split('.'), Has.Length.EqualTo(3));
            return Json(new GitRepositoryListResponse([]));
        }));
        await using var app = CreateApp(http);
        var owner = new AuthenticatedUser(new Profile(ApiTestAuthentication.CreateProfileResponse(), app), auth, app, DateTime.UtcNow);
        ApiTestAuthentication.SignIn(app, owner);

        Assert.That(await app.GetResource<HostedGitRepositoryService>().ListAsync(), Is.Empty);
    }

    [TestCase(401, "", true, HostedGitError.AuthenticationRequired)]
    [TestCase(404, "", true, HostedGitError.Unavailable)]
    [TestCase(404, "", false, HostedGitError.NotFound)]
    [TestCase(503, "", false, HostedGitError.Unavailable)]
    [TestCase(409, "Repository limit reached", true, HostedGitError.RepositoryLimit)]
    [TestCase(409, "Access token limit reached", false, HostedGitError.TokenLimit)]
    [TestCase(409, "The authenticated account changed; retry with the original account", true, HostedGitError.AccountChanged)]
    [TestCase(409, "Repository creation identifier conflicts with an existing repository", true, HostedGitError.Conflict)]
    public async Task Known_errors_have_localized_details(int status, string message, bool collection, HostedGitError expected)
    {
        using var http = new HttpClient(new DelegateHttpHandler(_ => Json(new { message }, (HttpStatusCode)status)));
        await using var app = CreateApp(http);
        ApiTestAuthentication.SignIn(app);
        var service = app.GetResource<HostedGitRepositoryService>();
        var failure = await Assert.ThrowsAsync<HostedGitApiException>(async () =>
        {
            if (collection) await service.ListAsync();
            else await service.GetAsync(Guid.Parse(RepositoryId));
        });
        Assert.That(failure!.Error, Is.EqualTo(expected));
        Assert.That(failure.StatusCode, Is.EqualTo((HttpStatusCode)status));
        Assert.That(failure.Message, Is.Not.Empty);
    }

    [Test]
    public async Task Signed_out_and_stale_owner_requests_do_not_reach_the_server()
    {
        using var http = new HttpClient(new DelegateHttpHandler((_, _) => throw new AssertionException("Unexpected HTTP request")));
        await using var app = CreateApp(http);
        var service = app.GetResource<HostedGitRepositoryService>();
        Assert.That((await Assert.ThrowsAsync<HostedGitApiException>(() => service.ListAsync()))!.Error,
            Is.EqualTo(HostedGitError.AuthenticationRequired));
        var originalOwner = ApiTestAuthentication.SignIn(app, id: "original");
        ApiTestAuthentication.SignIn(app, id: "different");
        Assert.That((await Assert.ThrowsAsync<HostedGitApiException>(() => service.CreateAsync("Repository", Guid.NewGuid(), originalOwner)))!.Error,
            Is.EqualTo(HostedGitError.AccountChanged));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Desktop_token_is_revoked_when_setup_fails_or_is_canceled(bool canceled)
    {
        var calls = new List<HttpMethod>();
        using var http = new HttpClient(new DelegateHttpHandler(request =>
        {
            calls.Add(request.Method);
            return request.Method == HttpMethod.Post
                ? Json(Token(), HttpStatusCode.Created)
                : new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        await using var app = CreateApp(http);
        var owner = ApiTestAuthentication.SignIn(app);
        using var cancellation = new CancellationTokenSource();
        var service = app.GetResource<HostedGitRepositoryService>();
        var failure = await Assert.CatchAsync(async () => await service.UseDesktopTokenAsync<bool>(
            Guid.Parse(RepositoryId), owner, (secret, token) =>
            {
                Assert.That(secret, Is.EqualTo(AccessToken));
                if (canceled)
                {
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                }
                throw new IOException("Local setup failed");
            }, cancellation.Token));

        Assert.That(failure, canceled ? Is.InstanceOf<OperationCanceledException>() : Is.TypeOf<IOException>());
        Assert.That(calls, Is.EqualTo(new[] { HttpMethod.Post, HttpMethod.Delete }));
    }

    [Test]
    public void Names_match_the_web_rules()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HostedGitRepositoryService.IsValidRepositoryName("  アニメーション  "), Is.True);
            Assert.That(HostedGitRepositoryService.IsValidRepositoryName(new string('a', 80)), Is.True);
            Assert.That(HostedGitRepositoryService.IsValidRepositoryName(new string('a', 81)), Is.False);
            Assert.That(HostedGitRepositoryService.IsValidRepositoryName(" "), Is.False);
            Assert.That(HostedGitRepositoryService.IsValidRepositoryName("a/b"), Is.False);
            Assert.That(HostedGitRepositoryService.IsValidRepositoryName("a\\b"), Is.False);
            Assert.That(HostedGitRepositoryService.IsValidRepositoryName("a\tb"), Is.False);
            string name = HostedGitRepositoryService.CreateDesktopTokenName(new string('d', 79) + "\nb");
            Assert.That(name, Has.Length.EqualTo(80));
            Assert.That(name, Does.StartWith("Beutl Desktop (").And.EndWith(")"));
            Assert.That(HostedGitRepositoryService.CreateDesktopTokenName("test\r\npc"), Is.EqualTo("Beutl Desktop (testpc)"));
        });
    }

    [Test]
    public async Task Thumbnail_upload_and_download_use_authenticated_binary_endpoints()
    {
        Guid id = Guid.Parse(RepositoryId);
        byte[] pngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02];
        DateTimeOffset uploadedAt = DateTimeOffset.Parse("2026-11-25T01:00:00Z");
        byte[]? uploaded = null;
        string? uploadContentType = null;
        bool returnNotFound = false;

        using var http = new HttpClient(new DelegateHttpHandler(async request =>
        {
            Assert.That(request.Headers.Authorization?.ToString(), Is.EqualTo("Bearer desktop-token"));
            Assert.That(request.RequestUri!.AbsolutePath, Is.EqualTo($"/api/v3/repos/{RepositoryId}/thumbnail"));
            if (request.Method == HttpMethod.Put)
            {
                uploadContentType = request.Content!.Headers.ContentType?.MediaType;
                uploaded = await request.Content.ReadAsByteArrayAsync();
                return Json(Repository() with { ThumbnailUpdatedAt = uploadedAt });
            }

            if (returnNotFound)
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(pngBytes),
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            return response;
        }));
        await using var app = CreateApp(http);
        AuthenticatedUser owner = ApiTestAuthentication.SignIn(app, id: "owner", token: "desktop-token");
        var service = app.GetResource<HostedGitRepositoryService>();

        GitRepositorySummary updated = await service.UploadThumbnailAsync(id, pngBytes, owner);
        Assert.Multiple(() =>
        {
            Assert.That(uploadContentType, Is.EqualTo("image/png"));
            Assert.That(uploaded, Is.EqualTo(pngBytes));
            Assert.That(updated.ThumbnailUpdatedAt, Is.EqualTo(uploadedAt));
        });

        Assert.That(await service.DownloadThumbnailAsync(id, owner), Is.EqualTo(pngBytes));

        returnNotFound = true;
        Assert.That(await service.DownloadThumbnailAsync(id, owner), Is.Null);

        Assert.Throws<ArgumentException>(() => service.UploadThumbnailAsync(id, ReadOnlyMemory<byte>.Empty, owner));
        Assert.Throws<ArgumentException>(() => service.UploadThumbnailAsync(
            id, new byte[HostedGitRepositoryService.MaxThumbnailBytes + 1], owner));
    }

    [Test]
    public async Task Thumbnail_download_times_out_when_response_body_stalls()
    {
        Guid id = Guid.Parse(RepositoryId);
        using var http = new HttpClient(new DelegateHttpHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StallingStreamContent(),
            })));
        await using var app = CreateApp(http);
        AuthenticatedUser owner = ApiTestAuthentication.SignIn(app, id: "owner", token: "desktop-token");
        var service = app.GetResource<HostedGitRepositoryService>();
        service.ThumbnailDownloadTimeout = TimeSpan.FromMilliseconds(80);

        var failure = await Assert.ThrowsAsync<HostedGitApiException>(() => service.DownloadThumbnailAsync(id, owner));
        Assert.That(failure!.Error, Is.EqualTo(HostedGitError.Unavailable));
    }

    private sealed class StallingStreamContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => Task.CompletedTask;

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new StallingStream());

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class StallingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => new(Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ContinueWith(static _ => 0, TaskContinuationOptions.OnlyOnRanToCompletion));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
