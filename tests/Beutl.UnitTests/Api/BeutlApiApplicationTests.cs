using System.Reflection;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Reactive.Bindings;

namespace Beutl.UnitTests.Api;

[TestFixture]
public sealed class BeutlApiApplicationTests
{
    [Test]
    public void Constructor_RegistersProvidedExtensionProvider()
    {
        using var httpClient = new HttpClient();
        var extensionProvider = new ExtensionProvider();
        using var app = new BeutlApiApplication(httpClient, extensionProvider);

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
    public void Constructor_UsesProductionApiOrigin()
    {
        using var httpClient = new HttpClient();

        using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(httpClient.BaseAddress, Is.EqualTo(new Uri("https://beutl.beditor.net/")));
            Assert.That(BeutlApiApplication.UserFileName, Is.EqualTo("user.json"));
        }
    }

    [Test]
    public void Constructor_RegistersBuiltInJobKindsThroughTheDescriptorRegistry()
    {
        using var httpClient = new HttpClient();
        using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
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
    public void ReadUserAsync_PreCanceledRequestStopsBeforeFileAccess()
    {
        using var httpClient = new HttpClient();
        using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await app.ReadUserAsync(cancellationTokenSource.Token));
    }

    [Test]
    public void Dispose_IsIdempotentAndRejectsFurtherResourceResolution()
    {
        using var httpClient = new HttpClient();
        using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        _ = app.GetResource<IAiJobMonitor>();

        Assert.DoesNotThrow(() => app.Dispose());
        Assert.DoesNotThrow(() => app.Dispose());
        Assert.Throws<ObjectDisposedException>(() => app.GetResource<IAiImageGenerationService>());
    }

    [Test]
    public async Task AuthenticatedRequest_KeepsCapturedBearerAndEndsWithItsSession()
    {
        using var httpClient = new HttpClient();
        using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
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
        using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
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

        app.Dispose();

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

}
