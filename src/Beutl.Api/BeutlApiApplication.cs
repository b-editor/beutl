using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Reactive.Bindings;
using Refit;
using IPackagesClient = Beutl.Api.Clients.IPackagesClient;
using IReleasesClient = Beutl.Api.Clients.IReleasesClient;
using IUsersClient = Beutl.Api.Clients.IUsersClient;

namespace Beutl.Api;

public partial class BeutlApiApplication : IAsyncDisposable
{
#if false
    public const string BaseUrl = "http://localhost:3001";
    public const string UserFileName = "user.local.json";
#else
    public const string BaseUrl = "https://beutl.beditor.net";
    public const string UserFileName = "user.json";
#endif
    private readonly HttpClient _httpClient;
    private readonly ExtensionProvider _extensionProvider;
    private readonly Func<HttpClient> _packageInstallerHttpClientFactory;
    private readonly Action<AuthenticatedUser> _persistAuthenticatedUser;
    private readonly ReactivePropertySlim<AuthenticatedUser?> _authenticatedUser = new();
    private readonly ReadOnlyReactivePropertySlim<AuthenticatedUser?> _readOnlyAuthenticatedUser;
    private readonly Dictionary<Type, Lazy<object>> _services = [];
    private readonly object _disposeGate = new();
    private readonly object _authenticationGate = new();
    private readonly SemaphoreSlim _authenticationRefreshGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly IDisposable _authenticationSubscription;
    private static readonly ILogger s_logger = Log.CreateLogger<BeutlApiApplication>();
    private volatile bool _disposed;
    private Task? _disposeTask;
    private CancellationTokenSource? _authenticationSessionCts;
    private long _authenticationGeneration;
    private long _authenticationAttemptVersion;
    private static readonly AsyncLazy<AssetMetadataJson?> s_metadata = new(async () =>
    {
        s_logger.LogInformation("Loading asset metadata");
        string path = Path.Combine(AppContext.BaseDirectory, "asset_metadata.json");
        if (!File.Exists(path))
        {
            s_logger.LogWarning("Asset metadata not found");
            return null;
        }
        string json = await File.ReadAllTextAsync(path);
        var metadata = JsonSerializer.Deserialize<AssetMetadataJson>(json);
        s_logger.LogInformation("Loaded asset metadata: {Metadata}", json);

        return metadata;
    });

    public BeutlApiApplication(HttpClient httpClient, ExtensionProvider extensionProvider)
        : this(httpClient, extensionProvider, static () => new HttpClient())
    {
    }

    internal BeutlApiApplication(
        HttpClient httpClient,
        ExtensionProvider extensionProvider,
        Func<HttpClient> packageInstallerHttpClientFactory,
        Action<AuthenticatedUser>? persistAuthenticatedUser = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(extensionProvider);
        ArgumentNullException.ThrowIfNull(packageInstallerHttpClientFactory);

        _httpClient = httpClient;
        _extensionProvider = extensionProvider;
        _packageInstallerHttpClientFactory = packageInstallerHttpClientFactory;
        _persistAuthenticatedUser = persistAuthenticatedUser ?? PersistAuthenticatedUser;
        _authenticationSubscription = _authenticatedUser.Subscribe(HandleAuthenticatedUserChanged);
        _readOnlyAuthenticatedUser = _authenticatedUser.ToReadOnlyReactivePropertySlim();
        _httpClient.BaseAddress = new Uri(BaseUrl);
        App = RestService.For<IAppClient>(_httpClient);
        Packages = RestService.For<IPackagesClient>(_httpClient);
        Releases = RestService.For<IReleasesClient>(_httpClient);
        Files = RestService.For<IFilesClient>(_httpClient);
        Storage = RestService.For<IStorageClient>(_httpClient);
        Users = RestService.For<IUsersClient>(_httpClient);
        Account = RestService.For<IAccountClient>(_httpClient);
        Discover = RestService.For<IDiscoverClient>(_httpClient);
        Library = RestService.For<ILibraryClient>(_httpClient);
        Ai = RestService.For<IAiClient>(_httpClient);

        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        string culture = viewConfig.UICulture.Name;
        if (!string.IsNullOrWhiteSpace(culture))
        {
            _httpClient.DefaultRequestHeaders.AcceptLanguage.Clear();
            _httpClient.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(culture));
        }

        RegisterAll();
    }

    public ActivitySource ActivitySource { get; } = new("Beutl.Api.Client", BeutlApplication.Version);

    public IPackagesClient Packages { get; }

    public IReleasesClient Releases { get; }

    public IUsersClient Users { get; }

    public IAccountClient Account { get; }

    public IFilesClient Files { get; }

    internal IStorageClient Storage { get; }

    public IDiscoverClient Discover { get; }

    public ILibraryClient Library { get; }

    internal IAiClient Ai { get; }

    public IAppClient App { get; }

    internal HttpClient HttpClient => _httpClient;

    public MyAsyncLock Lock { get; } = new();

    public IReadOnlyReactiveProperty<AuthenticatedUser?> AuthenticatedUser => _readOnlyAuthenticatedUser;

    public bool IsDisposed => _disposed;

    internal TimeSpan DisposalDeadline { get; set; } = TimeSpan.FromSeconds(30);

    // Check for updates. Return AppUpdateResponse when this application has asset metadata;
    // otherwise, return CheckForUpdatesResponse.
    public async Task<(CheckForUpdatesResponse? V1, AppUpdateResponse? V3)> CheckForUpdatesAsync(
        string version,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource lifetimeCts = CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = lifetimeCts.Token;
        var metadata = await LoadMetadata().WaitAsync(token);
        if (metadata == null)
        {
            var updateResponse = await App.CheckForUpdates(version, token);
            token.ThrowIfCancellationRequested();
            return (updateResponse, null);
        }

        var update = await App.GetUpdate(
            version, metadata.Type, metadata.OS, metadata.Arch,
            metadata.Standalone, "false", token);
        token.ThrowIfCancellationRequested();
        return (null, update);
    }

    public static async Task<AssetMetadataJson?> LoadMetadata()
    {
        return await s_metadata;
    }

    public T GetResource<T>()
        where T : IBeutlApiResource
    {
        lock (_disposeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_services.TryGetValue(typeof(T), out Lazy<object>? lazy))
            {
                return (T)lazy.Value;
            }

            foreach (KeyValuePair<Type, Lazy<object>> item in _services)
            {
                if (item.Key.IsAssignableTo(typeof(T)))
                {
                    return (T)item.Value.Value;
                }
            }

            throw new Exception("Resource not found");
        }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? proxy = null;
        Task disposeTask;
        lock (_disposeGate)
        {
            if (_disposeTask is null)
            {
                proxy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _disposeTask = proxy.Task;
                _disposed = true;
            }
            disposeTask = _disposeTask;
        }

        if (proxy is not null)
        {
            Task teardown = Task.Run(DisposeCoreAsync);
            _ = CompleteDisposeAsync(proxy, teardown);
        }

        return new ValueTask(disposeTask);
    }

    private async Task CompleteDisposeAsync(TaskCompletionSource proxy, Task teardown)
    {
        try
        {
            await teardown.WaitAsync(DisposalDeadline).ConfigureAwait(false);
            proxy.TrySetResult();
        }
        catch (TimeoutException)
        {
            s_logger.LogWarning(
                "API application shutdown exceeded {Deadline}; cleanup will continue after callbacks and active leases drain.",
                DisposalDeadline);
            proxy.TrySetResult();
            _ = ObserveDeferredTeardownAsync(teardown);
        }
        catch (Exception ex)
        {
            proxy.TrySetException(ex);
        }
    }

    private async Task DisposeCoreAsync()
    {
        List<object> disposableResources;
        lock (_disposeGate)
        {
            disposableResources = _services.Values
                .Where(lazy => lazy.IsValueCreated)
                .Select(lazy => lazy.Value)
                .Where(resource => resource is IDisposable or IAsyncDisposable)
                .Distinct(ReferenceEqualityComparer.Instance)
                .Reverse()
                .ToList();
        }

        Exception? cancellationFailure = null;
        try
        {
            _lifetimeCts.Cancel();
        }
        catch (Exception ex)
        {
            cancellationFailure = ex;
        }

        CancellationTokenSource? authenticationSession;
        lock (_authenticationGate)
        {
            authenticationSession = _authenticationSessionCts;
            _authenticationSessionCts = null;
            _authenticationGeneration++;
            _authenticationAttemptVersion++;
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }

        try
        {
            authenticationSession?.Cancel();
        }
        catch (Exception ex)
        {
            cancellationFailure ??= ex;
        }

        await DisposeResourcesAndStateAsync(disposableResources, authenticationSession)
            .ConfigureAwait(false);

        if (cancellationFailure is not null)
            throw cancellationFailure;
    }

    private async Task DisposeResourcesAndStateAsync(
        IReadOnlyList<object> disposableResources,
        CancellationTokenSource? authenticationSession)
    {
        foreach (object resource in disposableResources)
        {
            try
            {
                if (resource is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                else
                    ((IDisposable)resource).Dispose();
            }
            catch (Exception ex)
            {
                s_logger.LogWarning(
                    ex,
                    "Failed to dispose API resource {ResourceType}.",
                    resource.GetType());
            }
        }

        TryDispose(authenticationSession, "authentication session");
        TryDispose(_authenticationSubscription, "authentication subscription");
        TryDispose(_readOnlyAuthenticatedUser, "authenticated-user projection");
        TryDispose(_authenticatedUser, "authenticated-user state");
        TryDispose(_lifetimeCts, "application cancellation source");
        TryDispose(ActivitySource, "activity source");
    }

    private static void TryDispose(IDisposable? disposable, string name)
    {
        try
        {
            disposable?.Dispose();
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Failed to dispose API application {ResourceName}.", name);
        }
    }

    private static async Task ObserveDeferredTeardownAsync(Task teardown)
    {
        try
        {
            await teardown.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Deferred API application cleanup failed.");
        }
    }

    private void RegisterAll()
    {
        Register(() => new DiscoverService(this));
        Register(() => _extensionProvider);
        Register(() => new ContextCommandSettingsStore());
        Register(() => new ContextCommandHandlerRegistry());
        Register(() => new ContextCommandManager(
            GetResource<ContextCommandSettingsStore>(),
            GetResource<ContextCommandHandlerRegistry>()));
        Register(() => new InstalledPackageRepository());
        Register(() => new AcceptedLicenseManager());
        Register(() => new PackageChangesQueue());
        Register(() => new LibraryService(this));
        Register(() => new AiEntitlementStore(this));
        Register(() => new AiJobChangeNotifier());
        Register(() => new AiEntitlementService(
            this,
            GetResource<AiEntitlementStore>()));
        Register<IAiOperationCapabilitySchemaProvider>(() =>
            new AiOperationCapabilitySchemaRegistry(GetResource<IExtensionRegistry>()));
        Register(() => new AiModelCatalogService(
            this,
            capabilitySchemas: GetResource<IAiOperationCapabilitySchemaProvider>()));
        Register(() => new AiOperationAvailabilityService(this));
        Register(() => new AiImageGenerationService(
            this,
            GetResource<AiJobChangeNotifier>()));
        Register(() => new AiImageEditingService(
            this,
            GetResource<AiJobChangeNotifier>()));
        Register(() => new AiTranscriptionService(
            this,
            GetResource<AiJobChangeNotifier>()));
        Register(() => new AiCaptionTranslationService(
            this,
            GetResource<AiJobChangeNotifier>()));
        Register(() => new AiVideoService(
            this,
            GetResource<AiJobChangeNotifier>()));
        Register(() => new AuthenticatedContentService(this));
        Register(() => new AiJobClient(this));
        Register(() => new AiRetryAttemptContext(
            new FileAiRetryKeyStore(Path.Combine(
                BeutlEnvironment.GetHomeDirectoryPath(),
                "ai")),
            () => AuthenticatedUser.Value is { } user
                ? new AiAuthenticatedRequestIdentity(user.Profile.Id, user)
                : null));
        Register<IAiJobKindRegistry>(() => AiJobKindRegistry.CreateBuiltIn(
            GetResource<IAiImageGenerationService>(),
            GetResource<IAiVideoService>(),
            GetResource<IAiEntitlementService>(),
            GetResource<IAiOperationAvailabilityService>(),
            GetResource<IAiModelCatalogService>(),
            GetResource<AiRetryAttemptContext>(),
            GetResource<IExtensionRegistry>()));
        Register(() => new AiJobMonitor(
            this,
            GetResource<IAiJobClient>(),
            GetResource<IAiJobKindRegistry>(),
            GetResource<AiJobChangeNotifier>().Changes,
            TimeSpan.FromSeconds(5)));
        Register(CreatePackageInstaller);
        Register(() =>
        {
            // Unload diagnostics take a heavy ClrMD self-snapshot and write a dump; they are a development-only aid,
            // so Release builds wire null and neither snapshot, dump, nor log on an unload failure.
            ILoadContextUnloadDiagnostics? unloadDiagnostics = null;
#if DEBUG
            unloadDiagnostics = new ClrmdLoadContextUnloadDiagnostics();
#endif
            return new PackageManager(
                GetResource<InstalledPackageRepository>(), GetResource<ExtensionProvider>(),
                GetResource<ContextCommandManager>(), this, unloadDiagnostics);
        });
    }

    private void Register<T>(Func<T> factory)
        where T : IBeutlApiResource
    {
        _services.Add(typeof(T), new Lazy<object>(() => factory()));
    }

    private PackageInstaller CreatePackageInstaller()
    {
        HttpClient httpClient = _packageInstallerHttpClientFactory()
            ?? throw new InvalidOperationException("The package installer HTTP client factory returned null.");
        try
        {
            return new PackageInstaller(
                httpClient,
                ownsHttpClient: true,
                GetResource<InstalledPackageRepository>(),
                this);
        }
        catch
        {
            httpClient.Dispose();
            throw;
        }
    }

    internal CancellationTokenSource CreateLifetimeLinkedTokenSource(CancellationToken cancellationToken)
    {
        lock (_disposeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCts.Token);
        }
    }

    internal (T Resource, CancellationTokenSource Lifetime) GetResourceWithLifetime<T>(
        CancellationToken cancellationToken) where T : IBeutlApiResource
    {
        lock (_disposeGate)
        {
            T resource = GetResource<T>();
            return (resource, CreateLifetimeLinkedTokenSource(cancellationToken));
        }
    }
}
