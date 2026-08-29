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
    public async Task DisposeAsync_PublishesOneTaskBeforeLifetimeCancellationCanReenter()
    {
        using var httpClient = new HttpClient();
        var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var resource = new ProbeResource();
        RegisterResource(app, resource);
        using CancellationTokenSource lifetime =
            app.CreateLifetimeLinkedTokenSource(CancellationToken.None);
        Task? reentrantDisposal = null;
        using CancellationTokenRegistration registration = lifetime.Token.Register(() =>
            reentrantDisposal = app.DisposeAsync().AsTask());

        Task disposal = app.DisposeAsync().AsTask();
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reentrantDisposal, Is.SameAs(disposal));
            Assert.That(resource.DisposeCount, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task DisposeAsync_SynchronousLifetimeCallbackWaitCompletesAtTheSharedDeadline()
    {
        using var httpClient = new HttpClient();
        var app = new BeutlApiApplication(httpClient, new ExtensionProvider())
        {
            DisposalDeadline = TimeSpan.FromMilliseconds(100),
        };
        var resource = new ProbeResource();
        RegisterResource(app, resource);
        using CancellationTokenSource lifetime =
            app.CreateLifetimeLinkedTokenSource(CancellationToken.None);
        var callbackReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration = lifetime.Token.Register(() =>
        {
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            callbackReturned.TrySetResult();
        });

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await app.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        stopwatch.Stop();
        await callbackReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => resource.DisposeCount == 1, TimeSpan.FromSeconds(5));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
            Assert.That(resource.DisposeCount, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task DisposeAsync_CancellationCallbackFailureCannotSkipResources()
    {
        using var httpClient = new HttpClient();
        var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var resource = new ProbeResource();
        RegisterResource(app, resource);
        using CancellationTokenSource lifetime =
            app.CreateLifetimeLinkedTokenSource(CancellationToken.None);
        using CancellationTokenRegistration registration = lifetime.Token.Register(static () =>
            throw new InvalidOperationException("callback failed"));

        Assert.CatchAsync<Exception>(async () =>
            await app.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.That(resource.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task DisposeAsync_DeadlineLeavesAnActiveResourceAliveUntilItsLeaseDrains()
    {
        using var httpClient = new HttpClient();
        var app = new BeutlApiApplication(httpClient, new ExtensionProvider())
        {
            DisposalDeadline = TimeSpan.FromMilliseconds(100),
        };
        var resource = new BlockingResource();
        RegisterResource(app, resource);

        Task disposal = app.DisposeAsync().AsTask();
        await resource.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(resource.DisposeCompleted, Is.False,
            "The shutdown deadline must not tear down a resource while its lease is active.");

        resource.Release.TrySetResult();
        await resource.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(resource.DisposeCompleted, Is.True);
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
    public async Task AuthenticatedRequestScopeRejectsAccountSwitchBeforeDispatch()
    {
        using var httpClient = new HttpClient();
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        AuthenticatedUser first = SetAuthenticatedUser(app, "first-user", "first-token");
        using IDisposable scope = AiAuthenticatedRequestScope.Enter(first);
        SetAuthenticatedUser(app, "second-user", "second-token");
        bool invoked = false;

        Assert.ThrowsAsync<AuthenticationRequiredException>(() =>
            app.SendAuthenticatedAsync(
                (_, _) =>
                {
                    invoked = true;
                    return Task.FromResult(1);
                },
                CancellationToken.None));
        Assert.That(invoked, Is.False);
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

    private static AuthenticatedUser SetAuthenticatedUser(
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
        return user;
    }

    private static void RegisterResource<T>(BeutlApiApplication app, T resource)
        where T : class, IBeutlApiResource
    {
        FieldInfo field = typeof(BeutlApiApplication).GetField(
            "_services",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var services = (Dictionary<Type, Lazy<object>>)field.GetValue(app)!;
        services.Add(typeof(T), new Lazy<object>(() => resource));
        _ = app.GetResource<T>();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (!condition())
        {
            await Task.Delay(10, cancellation.Token);
        }
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

    private sealed class ProbeResource : IBeutlApiResource, IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingResource : IBeutlApiResource, IAsyncDisposable
    {
        public TaskCompletionSource DisposeStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Disposed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool DisposeCompleted { get; private set; }

        public async ValueTask DisposeAsync()
        {
            DisposeStarted.TrySetResult();
            await Release.Task;
            DisposeCompleted = true;
            Disposed.TrySetResult();
        }
    }

}
