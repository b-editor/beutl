using System.Net;
using System.Reflection;
using System.Text;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Reactive.Bindings;

namespace Beutl.UnitTests.Api;

[TestFixture]
[NonParallelizable]
public sealed class BeutlApiApplicationTests
{
    [Test]
    public async Task Constructor_RegistersProvidedExtensionProvider()
    {
        using var httpClient = new HttpClient();
        var extensionProvider = new ExtensionProvider();
        await using var app = new BeutlApiApplication(httpClient, extensionProvider);

        ExtensionProvider registeredProvider = app.GetResource<ExtensionProvider>();
        PackageManager packageManager = app.GetResource<PackageManager>();

        Assert.That(registeredProvider, Is.SameAs(extensionProvider));
        Assert.That(packageManager.ExtensionRegistry, Is.SameAs(extensionProvider));
        Assert.That(
            app.AuthenticatedUser,
            Is.Not.InstanceOf<global::Reactive.Bindings.ReactivePropertySlim<
                Beutl.Api.Objects.AuthenticatedUser?>>());
    }

    [Test]
    public async Task Constructor_UsesProductionApiOrigin()
    {
        using var httpClient = new HttpClient();

        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(httpClient.BaseAddress, Is.EqualTo(new Uri("https://beutl.beditor.net/")));
            Assert.That(BeutlApiApplication.UserFileName, Is.EqualTo("user.json"));
        }
    }

    [Test]
    public async Task Constructor_RegistersBuiltInJobKindsThroughTheDescriptorRegistry()
    {
        using var httpClient = new HttpClient();
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        IAiJobKindRegistry registry = app.GetResource<IAiJobKindRegistry>();
        var runningVideo = new AiJob(
            new AiJobId("job-1"),
            AiJobKinds.Video,
            AiJobStatuses.Running,
            null,
            null,
            null,
            null,
            false,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        AiJobStatusSemantics status = registry.GetStatus(runningVideo);
        Assert.That(registry.TryAcquire(AiJobKinds.Video, out IAiJobKindLease? lease), Is.True);
        using (lease)
        using (Assert.EnterMultipleScope())
        {
            Assert.That(status.IsTerminal, Is.False);
            Assert.That(status.ShouldPoll, Is.True);
            Assert.That(status.Outcome, Is.Null);
            Assert.That(lease!.Descriptor.RefreshHandler, Is.Not.Null);
            Assert.That(lease.Descriptor.RetryHandler, Is.Not.Null);
        }
    }

    [Test]
    public void Constructor_NullHttpClient_Throws()
    {
        var extensionProvider = new ExtensionProvider();

        Assert.Throws<ArgumentNullException>(() => _ = new BeutlApiApplication(null!, extensionProvider));
    }

    [Test]
    public void Constructor_NullExtensionProvider_Throws()
    {
        using var httpClient = new HttpClient();

        Assert.Throws<ArgumentNullException>(() => _ = new BeutlApiApplication(httpClient, null!));
    }

    [Test]
    public void ToServerType_Flatpak_MapsToZip()
    {
        Assert.That(BeutlApiApplication.ToServerType("flatpak"), Is.EqualTo("zip"));
    }

    [TestCase("zip")]
    [TestCase("debian")]
    [TestCase("installer")]
    [TestCase("app")]
    public void ToServerType_ArchiveType_PassesThrough(string type)
    {
        Assert.That(BeutlApiApplication.ToServerType(type), Is.EqualTo(type));
    }

    [Test]
    public async Task CheckForUpdatesAsync_WithFlatpakMetadata_SendsZipQueryType()
    {
        string metadataPath = Path.Combine(AppContext.BaseDirectory, "asset_metadata.json");
        try
        {
            await File.WriteAllTextAsync(metadataPath, """
                {
                  "id": "test-id",
                  "os": "linux",
                  "arch": "x64",
                  "version": "2.0.0-preview.6",
                  "standalone": "true",
                  "type": "flatpak"
                }
                """);
            var handler = new CapturingHandler();
            using var httpClient = new HttpClient(handler);
            await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());

            var (v1, v3) = await app.CheckForUpdatesAsync("2.0.0-preview.6", CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(handler.LastRequestUri, Is.Not.Null);
                Assert.That(handler.LastRequestUri!.Query, Does.Contain("type=zip"));
                Assert.That(handler.LastRequestUri.Query, Does.Not.Contain("type=flatpak"));
                Assert.That(v1, Is.Null);
                Assert.That(v3, Is.Not.Null);
            }
        }
        finally
        {
            File.Delete(metadataPath);
        }
    }

    [Test]
    public async Task ReadUserAsync_PreCanceledRequestStopsBeforeFileAccess()
    {
        using var httpClient = new HttpClient();
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await app.ReadUserAsync(cancellationTokenSource.Token));
    }

    [Test]
    public async Task Dispose_IsIdempotentAndRejectsFurtherResourceResolution()
    {
        using var httpClient = new HttpClient();
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        _ = app.GetResource<IAiJobMonitor>();

        Assert.DoesNotThrowAsync(async () => await app.DisposeAsync());
        Assert.DoesNotThrowAsync(async () => await app.DisposeAsync());
        Assert.Throws<ObjectDisposedException>(() => app.GetResource<IAiImageGenerationService>());
    }

    [Test]
    public async Task DisposeAsync_WaitsForActiveExtensionDescriptorLease()
    {
        using var httpClient = new HttpClient();
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        IAiJobKindRegistry registry = app.GetResource<IAiJobKindRegistry>();
        Assert.That(registry.TryAcquire(AiJobKinds.Video, out IAiJobKindLease? lease), Is.True);

        Task disposal = app.DisposeAsync().AsTask();
        await Task.Yield();

        Assert.That(disposal.IsCompleted, Is.False);
        lease!.Dispose();
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Throws<ObjectDisposedException>(() => app.GetResource<IAiJobKindRegistry>());
    }

    [Test]
    public async Task AuthenticatedRequest_KeepsCapturedBearerAndEndsWithItsSession()
    {
        using var httpClient = new HttpClient();
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(app, "first-user", "first-token");
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? authorization = null;

        Task operation = app.SendAuthenticatedAsync(
            async (capturedAuthorization, cancellationToken) =>
            {
                authorization = capturedAuthorization;
                requestStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return 1;
            },
            CancellationToken.None);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        SetAuthenticatedUser(app, "second-user", "second-token");

        Assert.ThrowsAsync<AuthenticationRequiredException>(async () => await operation);
        Assert.That(authorization, Is.EqualTo("Bearer first-token"));
    }

    [Test]
    public async Task Dispose_CancelsActiveAuthenticatedRequestAndDisposesAiCapabilities()
    {
        using var httpClient = new HttpClient();
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(app, "test-user", "token");
        IAiEntitlementService service = app.GetResource<IAiEntitlementService>();
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task operation = app.SendAuthenticatedAsync(
            async (_, cancellationToken) =>
            {
                requestStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return 1;
            },
            CancellationToken.None);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await app.DisposeAsync();

        Assert.CatchAsync<OperationCanceledException>(async () => await operation);
        Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await service.RefreshAsync(CancellationToken.None));
    }

    private static void SetAuthenticatedUser(
        BeutlApiApplication app,
        string userId,
        string token)
    {
        var profile = new Profile(new ProfileResponse
        {
            Id = userId,
            Name = userId,
            DisplayName = userId,
            Bio = null,
            IconId = null,
            IconUrl = null,
        }, app);
        var user = new AuthenticatedUser(profile, new AuthResponse
        {
            Token = token,
            RefreshToken = $"{token}-refresh",
            Expiration = DateTime.UtcNow.AddHours(1),
        }, app, DateTime.UtcNow);
        FieldInfo field = typeof(BeutlApiApplication).GetField(
            "_authenticatedUser",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        ((ReactivePropertySlim<AuthenticatedUser?>)field.GetValue(app)!).Value = user;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {"latestVersion":"2.0.0-preview.7","url":"https://example.test/release","downloadUrl":null,"isLatest":false,"mustLatest":false}
                    """,
                    Encoding.UTF8,
                    "application/json"),
            });
        }
    }

}
