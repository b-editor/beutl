using System.Reflection;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Extensibility;
using Moq;
using Reactive.Bindings;

namespace Beutl.UnitTests.Api;

[TestFixture]
public sealed class BeutlApiApplicationTests
{
    [Test]
    public void Constructor_RegistersProvidedExtensionRegistryAbstraction()
    {
        using var httpClient = new HttpClient();
        IExtensionRegistry extensionRegistry = new Mock<IExtensionRegistry>().Object;
        using var app = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, extensionRegistry));

        IExtensionRegistry registeredRegistry = app.GetResource<IExtensionRegistry>();
        PackageManager packageManager = app.GetResource<PackageManager>();

        Assert.That(registeredRegistry, Is.SameAs(extensionRegistry));
        Assert.That(packageManager.ExtensionRegistry, Is.SameAs(extensionRegistry));
        Assert.That(
            app.AuthenticatedUser,
            Is.Not.InstanceOf<global::Reactive.Bindings.ReactivePropertySlim<
                Beutl.Api.Objects.AuthenticatedUser?>>());
    }

    [Test]
    public void Create_DefaultsToProductionOrigins()
    {
        using var httpClient = new HttpClient();

        using var app = BeutlApiApplication.Create(
            new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(app.ApiBaseUri, Is.EqualTo(new Uri("https://beutl.beditor.net/")));
            Assert.That(app.PortalBaseUri, Is.EqualTo(new Uri("https://beutl.beditor.net/")));
            Assert.That(httpClient.BaseAddress, Is.EqualTo(app.ApiBaseUri));
            Assert.That(
                app.AccountSettingsUri,
                Is.EqualTo(new Uri("https://beutl.beditor.net/account/manage")));
            Assert.That(
                app.GetAiPlanUri("ja"),
                Is.EqualTo(new Uri("https://beutl.beditor.net/ja/account/manage/ai-plan")));
        }
    }

    [Test]
    public void Create_RegistersBuiltInJobKindsThroughTheDescriptorRegistry()
    {
        using var httpClient = new HttpClient();
        using var app = BeutlApiApplication.Create(
            new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
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
    public void Create_PreservesCallerApiOriginAndAllowsIndependentPortalOrigin()
    {
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:4100/"),
        };
        var options = new BeutlApiApplicationOptions(httpClient, new ExtensionProvider())
        {
            PortalBaseUri = new Uri("http://localhost:4200/portal/"),
            AuthenticationStateFileName = "user.local.json",
        };

        using var app = BeutlApiApplication.Create(options);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(app.ApiBaseUri, Is.EqualTo(new Uri("http://localhost:4100/")));
            Assert.That(app.PortalBaseUri, Is.EqualTo(new Uri("http://localhost:4200/portal/")));
            Assert.That(
                app.AccountSettingsUri,
                Is.EqualTo(new Uri("http://localhost:4200/portal/account/manage")));
        }
    }

    [Test]
    public void Create_ExplicitApiOriginOverridesCallerBaseAddress()
    {
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://old.example.test/"),
        };
        var options = new BeutlApiApplicationOptions(httpClient, new ExtensionProvider())
        {
            ApiBaseUri = new Uri("https://api.example.test/"),
        };

        using var app = BeutlApiApplication.Create(options);

        Assert.That(app.ApiBaseUri, Is.EqualTo(new Uri("https://api.example.test/")));
        Assert.That(httpClient.BaseAddress, Is.EqualTo(app.ApiBaseUri));
    }

    [Test]
    public void Create_RejectsUnsafeOriginsAndAuthenticationStatePaths()
    {
        using var httpClient = new HttpClient();

        Assert.Throws<ArgumentException>(() => BeutlApiApplication.Create(
            new BeutlApiApplicationOptions(httpClient, new ExtensionProvider())
            {
                ApiBaseUri = new Uri("file:///tmp/api"),
            }));
        Assert.Throws<ArgumentException>(() => BeutlApiApplication.Create(
            new BeutlApiApplicationOptions(httpClient, new ExtensionProvider())
            {
                ApiBaseUri = new Uri("https://api.example.test/prefix/"),
            }));
        Assert.Throws<ArgumentException>(() => BeutlApiApplication.Create(
            new BeutlApiApplicationOptions(httpClient, new ExtensionProvider())
            {
                AuthenticationStateFileName = "../user.json",
            }));
    }

    [Test]
    public void Constructor_NullHttpClient_Throws()
    {
        var extensionProvider = new ExtensionProvider();

        Assert.Throws<ArgumentNullException>(() => _ = BeutlApiApplication.Create(new BeutlApiApplicationOptions(null!, extensionProvider)));
    }

    [Test]
    public void Constructor_NullExtensionRegistry_Throws()
    {
        using var httpClient = new HttpClient();

        Assert.Throws<ArgumentNullException>(() => _ = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, null!)));
    }

    [Test]
    public void Create_OverridesCapabilitiesIndependentlyBeforeResolution()
    {
        using var httpClient = new HttpClient();
        var replacement = new StubTranslationService();
        var options = new BeutlApiApplicationOptions(httpClient, new ExtensionProvider());
        options.Resources.Replace<IAiCaptionTranslationService>(_ => replacement);

        using BeutlApiApplication app = BeutlApiApplication.Create(options);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(app.GetResource<IAiCaptionTranslationService>(), Is.SameAs(replacement));
            Assert.That(app.GetResource<IAiTranscriptionService>(), Is.Not.SameAs(replacement));
        }
    }

    [Test]
    public void Create_OverridesImageGenerationWithoutReplacingImageEditing()
    {
        using var httpClient = new HttpClient();
        var replacement = new StubImageGenerationService();
        var options = new BeutlApiApplicationOptions(httpClient, new ExtensionProvider());
        options.Resources.Replace<IAiImageGenerationService>(_ => replacement);

        using BeutlApiApplication app = BeutlApiApplication.Create(options);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(app.GetResource<IAiImageGenerationService>(), Is.SameAs(replacement));
            Assert.That(app.GetResource<IAiImageEditingService>(), Is.Not.SameAs(replacement));
        }
    }

    [Test]
    public void Create_UsesEffectiveExtensionRegistryAcrossRuntimeResources()
    {
        using var httpClient = new HttpClient();
        var configuredRegistry = new ExtensionProvider();
        var effectiveRegistry = new ExtensionProvider();
        var options = new BeutlApiApplicationOptions(httpClient, configuredRegistry);
        options.Resources.Replace<IExtensionRegistry>(_ => effectiveRegistry);

        using BeutlApiApplication app = BeutlApiApplication.Create(options);
        PackageManager packageManager = app.GetResource<PackageManager>();
        IAiJobKindRegistry jobKinds = app.GetResource<IAiJobKindRegistry>();
        var descriptor = new AiJobKindDescriptor(
            new AiJobKindId("tests.effective-registry"),
            new AiJobStatusMap([]));
        effectiveRegistry.AddExtensions(
            -10_001,
            [new TestAiJobKindExtension(descriptor)]);

        try
        {
            Assert.That(app.GetResource<IExtensionRegistry>(), Is.SameAs(effectiveRegistry));
            Assert.That(packageManager.ExtensionRegistry, Is.SameAs(effectiveRegistry));
            Assert.That(jobKinds.TryAcquire(descriptor.Kind, out IAiJobKindLease? lease), Is.True);
            using (lease)
            {
                Assert.That(lease!.Descriptor, Is.SameAs(descriptor));
            }
        }
        finally
        {
            effectiveRegistry.RemoveExtensions(-10_001);
        }
    }

    [Test]
    public void ReadUserAsync_PreCanceledRequestStopsBeforeFileAccess()
    {
        using var httpClient = new HttpClient();
        using var app = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await app.ReadUserAsync(cancellationTokenSource.Token));
    }

    [Test]
    public void Dispose_IsIdempotentAndRejectsFurtherResourceResolution()
    {
        using var httpClient = new HttpClient();
        using var app = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
        _ = app.GetResource<IAiJobMonitor>();

        Assert.DoesNotThrow(() => app.Dispose());
        Assert.DoesNotThrow(() => app.Dispose());
        Assert.Throws<ObjectDisposedException>(() => app.GetResource<IAiImageGenerationService>());
    }

    [Test]
    public async Task AuthenticatedRequest_KeepsCapturedBearerAndEndsWithItsSession()
    {
        using var httpClient = new HttpClient();
        using var app = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
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
        using var app = BeutlApiApplication.Create(new BeutlApiApplicationOptions(httpClient, new ExtensionProvider()));
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

    private sealed class StubTranslationService : IAiCaptionTranslationService
    {
        public Task<AiCaptionTranslationResponse> TranslateAsync(
            AiCaptionTranslationRequest request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class StubImageGenerationService : IAiImageGenerationService
    {
        public Task<AiImageResult> GenerateAsync(
            AiImageGenerationRequest request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class TestAiJobKindExtension(AiJobKindDescriptor descriptor) : AiJobKindExtension
    {
        public override AiJobKindDescriptor Descriptor { get; } = descriptor;

        public override AiJobKindRegistrationMode RegistrationMode
            => AiJobKindRegistrationMode.Add;
    }

}
