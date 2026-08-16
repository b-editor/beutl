using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Reactive.Bindings;
using Refit;
using Activity = System.Diagnostics.Activity;
using IPackagesClient = Beutl.Api.Clients.IPackagesClient;
using IReleasesClient = Beutl.Api.Clients.IReleasesClient;
using IUsersClient = Beutl.Api.Clients.IUsersClient;

namespace Beutl.Api;

public class BeutlApiApplication : IAsyncDisposable
{
#if false
    private const string BaseUrl = "http://localhost:3001";
    public const string UserFileName = "user.local.json";
#else
    private const string BaseUrl = "https://beutl.beditor.net";
    public const string UserFileName = "user.json";
#endif
    private readonly HttpClient _httpClient;
    private readonly ExtensionProvider _extensionProvider;
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
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(extensionProvider);

        _httpClient = httpClient;
        _extensionProvider = extensionProvider;
        _authenticationSubscription = _authenticatedUser.Subscribe(HandleAuthenticatedUserChanged);
        _readOnlyAuthenticatedUser = _authenticatedUser.ToReadOnlyReactivePropertySlim();
        _httpClient.BaseAddress = new Uri(BaseUrl);
        App = RestService.For<IAppClient>(_httpClient);
        Packages = RestService.For<IPackagesClient>(_httpClient);
        Releases = RestService.For<IReleasesClient>(_httpClient);
        Files = RestService.For<IFilesClient>(_httpClient);
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

    public IDiscoverClient Discover { get; }

    public ILibraryClient Library { get; }

    internal IAiClient Ai { get; }

    public IAppClient App { get; }

    internal HttpClient HttpClient => _httpClient;

    public MyAsyncLock Lock { get; } = new();

    public IReadOnlyReactiveProperty<AuthenticatedUser?> AuthenticatedUser => _readOnlyAuthenticatedUser;

    public bool IsDisposed => _disposed;

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
            version, ToServerType(metadata.Type), metadata.OS, metadata.Arch,
            metadata.Standalone, "false", token);
        token.ThrowIfCancellationRequested();
        return (null, update);
    }

    // The server accepts archive types, while local metadata records the Flatpak package type.
    // Flatpak releases are produced from the standalone zip archive used by the update endpoint.
    internal static string ToServerType(string type) => type == "flatpak" ? "zip" : type;

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
        lock (_disposeGate)
        {
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        List<object> disposableResources;
        lock (_disposeGate)
        {
            _disposed = true;
            disposableResources = _services.Values
                .Where(lazy => lazy.IsValueCreated)
                .Select(lazy => lazy.Value)
                .Where(resource => resource is IDisposable or IAsyncDisposable)
                .Distinct(ReferenceEqualityComparer.Instance)
                .Reverse()
                .ToList();
        }

        _lifetimeCts.Cancel();

        CancellationTokenSource? authenticationSession;
        lock (_authenticationGate)
        {
            authenticationSession = _authenticationSessionCts;
            _authenticationSessionCts = null;
            _authenticationGeneration++;
            _authenticationAttemptVersion++;
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }

        authenticationSession?.Cancel();

        foreach (object resource in disposableResources)
        {
            try
            {
                if (resource is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync();
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

        authenticationSession?.Dispose();
        _authenticationSubscription.Dispose();
        _readOnlyAuthenticatedUser.Dispose();
        _authenticatedUser.Dispose();
        _lifetimeCts.Dispose();
        ActivitySource.Dispose();
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
        Register<IAiJobKindRegistry>(() => AiJobKindRegistry.CreateBuiltIn(
            GetResource<IAiImageGenerationService>(),
            GetResource<IAiVideoService>(),
            GetResource<IAiEntitlementService>(),
            GetResource<IAiOperationAvailabilityService>(),
            GetResource<IExtensionRegistry>()));
        Register(() => new AiJobMonitor(
            this,
            GetResource<IAiJobClient>(),
            GetResource<IAiJobKindRegistry>(),
            GetResource<AiJobChangeNotifier>().Changes,
            TimeSpan.FromSeconds(5)));
        Register(() => new PackageInstaller(new HttpClient(), GetResource<InstalledPackageRepository>(), this));
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

    private void HandleAuthenticatedUserChanged(AuthenticatedUser? user)
    {
        CancellationTokenSource? previousSession;
        lock (_authenticationGate)
        {
            previousSession = _authenticationSessionCts;
            _authenticationSessionCts = user is null ? null : new CancellationTokenSource();
            _authenticationGeneration++;
            _authenticationAttemptVersion++;
            if (user is null)
            {
                _httpClient.DefaultRequestHeaders.Authorization = null;
            }
            else
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", user.Token);
            }
        }

        previousSession?.Cancel();
        previousSession?.Dispose();
    }

    private long BeginAuthenticationAttempt()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_authenticationGate)
            return ++_authenticationAttemptVersion;
    }

    private void CommitAuthenticatedUser(
        AuthenticatedUser user,
        long authenticationAttempt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_authenticationGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_authenticationAttemptVersion != authenticationAttempt)
                throw new AuthenticationRequiredException();

            _authenticatedUser.Value = user;
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", user.Token);
        }
    }

    private bool IsAuthenticationSessionCurrent(AuthenticatedUser user, long generation)
    {
        return !_disposed
            && generation == _authenticationGeneration
            && _authenticationSessionCts is not null
            && ReferenceEquals(_authenticatedUser.Value, user);
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

    internal async Task<AuthenticatedApiResult<T>> SendAuthenticatedAsync<T>(
        Func<string, CancellationToken, Task<T>> send,
        CancellationToken cancellationToken,
        AuthenticatedUser? expectedUser = null)
    {
        ArgumentNullException.ThrowIfNull(send);
        cancellationToken.ThrowIfCancellationRequested();

        using AuthenticatedSessionContext context = await CreateAuthenticatedSessionAsync(
                expectedUser,
                cancellationToken)
            .ConfigureAwait(false);
        try
        {
            T value = await send(context.Authorization, context.CancellationToken).ConfigureAwait(false);
            EnsureAuthenticationSessionCurrent(context, cancellationToken);
            return new AuthenticatedApiResult<T>(value, context.User);
        }
        catch (OperationCanceledException) when (
            context.AuthenticationToken.IsCancellationRequested
            && !context.ApplicationToken.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            throw new AuthenticationRequiredException();
        }
    }

    private async ValueTask<AuthenticatedSessionContext> CreateAuthenticatedSessionAsync(
        AuthenticatedUser? expectedUser,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource lifetimeCts = CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = lifetimeCts.Token;
        AuthenticatedUser user;
        lock (_authenticationGate)
        {
            user = _authenticatedUser.Value ?? throw new AuthenticationRequiredException();
            if (expectedUser is not null && !ReferenceEquals(user, expectedUser))
                throw new AuthenticationRequiredException();
        }

        await user.RefreshAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();

        lock (_disposeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            lock (_authenticationGate)
            {
                if (!ReferenceEquals(_authenticatedUser.Value, user)
                    || _authenticationSessionCts is null)
                {
                    throw new AuthenticationRequiredException();
                }

                long generation = _authenticationGeneration;
                CancellationToken authenticationToken = _authenticationSessionCts.Token;
                CancellationToken applicationToken = _lifetimeCts.Token;
                var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    applicationToken,
                    authenticationToken);
                return new AuthenticatedSessionContext(
                    user,
                    generation,
                    $"Bearer {user.Token}",
                    authenticationToken,
                    applicationToken,
                    linkedCancellation);
            }
        }
    }

    private void EnsureAuthenticationSessionCurrent(
        AuthenticatedSessionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.ApplicationToken.ThrowIfCancellationRequested();
        lock (_authenticationGate)
        {
            if (!IsAuthenticationSessionCurrent(context.User, context.Generation))
                throw new AuthenticationRequiredException();
        }
    }

    internal void CommitForAuthenticatedUser(
        AuthenticatedUser user,
        Action commit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(commit);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_authenticationGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!ReferenceEquals(_authenticatedUser.Value, user)
                || _authenticationSessionCts is null)
            {
                throw new AuthenticationRequiredException();
            }

            cancellationToken.ThrowIfCancellationRequested();
            commit();
        }
    }

    public void SignOut(bool deleteFile = true)
    {
        lock (_authenticationGate)
        {
            _authenticationAttemptVersion++;
            _authenticatedUser.Value = null;
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }
        if (deleteFile)
        {
            string fileName = Path.Combine(Helper.AppRoot, UserFileName);
            if (File.Exists(fileName))
            {
                File.Delete(fileName);
            }
        }
    }

    public Task<AuthenticatedUser> SignInWithGoogleAsync(CancellationToken cancellationToken)
    {
        return SignInExternalAsync("Google", cancellationToken);
    }

    public Task<AuthenticatedUser> SignInWithGitHubAsync(CancellationToken cancellationToken)
    {
        return SignInExternalAsync("GitHub", cancellationToken);
    }

    private async Task<AuthenticatedUser> SignInExternalAsync(string provider, CancellationToken cancellationToken)
    {
        using CancellationTokenSource lifetimeCts = CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = lifetimeCts.Token;
        using Activity? activity = ActivitySource.StartActivity("SignInExternalAsync", ActivityKind.Client);
        using (await Lock.LockAsync(token))
        {
            long authenticationAttempt = BeginAuthenticationAttempt();
            activity?.AddEvent(new("Entered_AsyncLock"));
            string continueUri = $"http://localhost:{GetRandomUnusedPort()}/__/auth/handler";
            CreateAuthUriResponse authUriRes = await Account.CreateAuthUri(
                new CreateAuthUriRequest { ContinueUri = continueUri },
                token);
            using HttpListener listener = StartListener($"{continueUri}/");
            activity?.AddEvent(new("Started_Listener"));

            string uri =
                $"{BaseUrl}/api/v2/identity/signInWith?provider={provider}&returnUrl={Uri.EscapeDataString(authUriRes.AuthUri)}";

            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true, Verb = "open" });

            string? code = await GetResponseFromListener(listener, token);
            activity?.AddEvent(new("Received_Code"));
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new Exception("The returned code was empty.");
            }

            AuthResponse authResponse = await Account.Exchange(
                new ExchangeRequest { Code = code, SessionId = authUriRes.SessionId },
                token);
            activity?.AddEvent(new("Done_CodeToJwtAsync"));

            ProfileResponse profileResponse = await Users.GetSelf(
                $"Bearer {authResponse.Token}",
                token);
            var profile = new Profile(profileResponse, this);
            var user = new AuthenticatedUser(profile, authResponse, this, DateTime.UtcNow);

            CommitAuthenticatedUser(user, authenticationAttempt, token);
            SaveUser(user);
            activity?.AddEvent(new("Saved_User"));
            return user;
        }
    }

    public async Task<AuthenticatedUser> SignInAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource lifetimeCts = CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = lifetimeCts.Token;
        using Activity? activity = ActivitySource.StartActivity("SignInAsync", ActivityKind.Client);
        using (await Lock.LockAsync(token))
        {
            long authenticationAttempt = BeginAuthenticationAttempt();
            activity?.AddEvent(new("Entered_AsyncLock"));
            string continueUri = $"http://localhost:{GetRandomUnusedPort()}/__/auth/handler";
            CreateAuthUriResponse authUriRes = await Account.CreateAuthUri(
                new CreateAuthUriRequest { ContinueUri = continueUri },
                token);
            using HttpListener listener = StartListener($"{continueUri}/");
            activity?.AddEvent(new("Started_Listener"));

            string uri = $"{BaseUrl}/account/signIn?returnUrl={Uri.EscapeDataString(authUriRes.AuthUri)}";

            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true, Verb = "open" });

            string? code = await GetResponseFromListener(listener, token);
            activity?.AddEvent(new("Received_Code"));
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new Exception("The returned code was empty.");
            }

            AuthResponse authResponse = await Account.Exchange(
                new ExchangeRequest { Code = code, SessionId = authUriRes.SessionId },
                token);
            activity?.AddEvent(new("Done_CodeToJwtAsync"));

            ProfileResponse profileResponse = await Users.GetSelf(
                $"Bearer {authResponse.Token}",
                token);
            var profile = new Profile(profileResponse, this);
            var user = new AuthenticatedUser(profile, authResponse, this, DateTime.UtcNow);

            CommitAuthenticatedUser(user, authenticationAttempt, token);
            SaveUser(user);
            activity?.AddEvent(new("Saved_User"));
            return user;
        }
    }

    public static void OpenAccountSettings()
    {
        Process.Start(new ProcessStartInfo($"{BaseUrl}/account/manage")
        {
            UseShellExecute = true,
            Verb = "open",
        });
    }

    internal async ValueTask RefreshAuthenticatedUserAsync(
        AuthenticatedUser user,
        bool force,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();
        using CancellationTokenSource lifetimeCts = CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken applicationToken = lifetimeCts.Token;
        long authenticationGeneration;
        CancellationToken sessionToken;
        CancellationTokenSource linkedCts;
        lock (_authenticationGate)
        {
            authenticationGeneration = _authenticationGeneration;
            if (_disposed
                || !ReferenceEquals(_authenticatedUser.Value, user)
                || _authenticationSessionCts is null)
            {
                throw new AuthenticationRequiredException();
            }

            sessionToken = _authenticationSessionCts.Token;
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                applicationToken,
                sessionToken);
        }

        using (linkedCts)
        {
            CancellationToken token = linkedCts.Token;
            bool gateEntered = false;
            try
            {
                await _authenticationRefreshGate.WaitAsync(token).ConfigureAwait(false);
                gateEntered = true;
                token.ThrowIfCancellationRequested();
                lock (_authenticationGate)
                {
                    if (!IsAuthenticationSessionCurrent(user, authenticationGeneration))
                        throw new AuthenticationRequiredException();
                }

                using Activity? activity = ActivitySource.StartActivity(
                    "AuthenticatedUser.Refresh",
                    ActivityKind.Client);
                (AuthResponse response, DateTime writeTime) = user.GetAuthenticationState();
                string fileName = Path.Combine(Helper.AppRoot, UserFileName);
                if (File.Exists(fileName))
                {
                    DateTime lastWriteTime = File.GetLastWriteTimeUtc(fileName);
                    if (writeTime < lastWriteTime)
                    {
                        AuthenticatedUser? fileUser = await ReadUserAsync(token).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        if (fileUser?.Profile.Id == user.Profile.Id)
                        {
                            (response, writeTime) = fileUser.GetAuthenticationState();
                        }
                        else if (fileUser is not null)
                        {
                            SignOutIfCurrent(user);
                            throw new InvalidOperationException(
                                "The user may have been changed in another process.");
                        }
                    }
                }

                bool isExpired = response.Expiration < DateTime.UtcNow;
                activity?.SetTag("force", force);
                activity?.SetTag("is_expired", isExpired);
                bool refreshed = false;
                if (force || isExpired)
                {
                    response = await Account.Refresh(
                            new RefreshTokenRequest
                            {
                                RefreshToken = response.RefreshToken,
                                Token = response.Token,
                            },
                            token)
                        .ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    refreshed = true;
                    activity?.AddEvent(new("Refreshed"));
                }

                lock (_authenticationGate)
                {
                    token.ThrowIfCancellationRequested();
                    if (!IsAuthenticationSessionCurrent(user, authenticationGeneration))
                        throw new AuthenticationRequiredException();

                    user.CommitAuthenticationState(response, writeTime);
                    _httpClient.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", response.Token);
                }

                if (refreshed)
                {
                    SaveUser(user);
                    activity?.AddEvent(new("Saved"));
                }
            }
            catch (OperationCanceledException) when (
                sessionToken.IsCancellationRequested
                && !applicationToken.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested)
            {
                throw new AuthenticationRequiredException();
            }
            finally
            {
                if (gateEntered)
                {
                    _authenticationRefreshGate.Release();
                }
            }
        }
    }

    private void SignOutIfCurrent(AuthenticatedUser user)
    {
        lock (_authenticationGate)
        {
            if (!ReferenceEquals(_authenticatedUser.Value, user))
                return;

            _authenticationAttemptVersion++;
            _authenticatedUser.Value = null;
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }
    }

    public void SaveUser()
    {
        if (_authenticatedUser.Value is { } user)
        {
            SaveUser(user);
        }
    }

    private void SaveUser(AuthenticatedUser user)
    {
        lock (_authenticationGate)
        {
            if (_disposed || !ReferenceEquals(_authenticatedUser.Value, user))
                return;

            (AuthResponse response, DateTime _) = user.GetAuthenticationState();
            string fileName = Path.Combine(Helper.AppRoot, UserFileName);
            using (FileStream stream = File.Create(fileName))
            {
                var obj = new JsonObject
                {
                    ["token"] = response.Token,
                    ["refresh_token"] = response.RefreshToken,
                    ["expiration"] = response.Expiration,
                    ["profile"] = JsonSerializer.SerializeToNode(user.Profile.Response.Value),
                };

                using var writer = new Utf8JsonWriter(stream);
                obj.WriteTo(writer);
            }

            user.SetWriteTime(File.GetLastWriteTimeUtc(fileName));
        }
    }

    public async Task RestoreUserAsync(Activity? activity, CancellationToken cancellationToken)
    {
        using CancellationTokenSource lifetimeCts = CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = lifetimeCts.Token;
        using (await Lock.LockAsync(token))
        {
            long authenticationAttempt = BeginAuthenticationAttempt();
            activity?.AddEvent(new("Entered_AsyncLock"));

            AuthenticatedUser? user = await ReadUserAsync(token);
            if (user != null)
            {
                CommitAuthenticatedUser(user, authenticationAttempt, token);
                try
                {
                    await user.RefreshAsync(token);
                    await user.Profile.RefreshAsync(token, self: true);
                    token.ThrowIfCancellationRequested();
                    SaveUser(user);
                }
                catch
                {
                    SignOutIfCurrent(user);
                    throw;
                }
            }
        }
    }

    public async ValueTask<AuthenticatedUser?> ReadUserAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource lifetimeCts = CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = lifetimeCts.Token;
        token.ThrowIfCancellationRequested();
        string fileName = Path.Combine(Helper.AppRoot, UserFileName);
        if (File.Exists(fileName))
        {
            JsonNode? node = JsonNode.Parse(await File.ReadAllTextAsync(fileName, token));
            token.ThrowIfCancellationRequested();
            DateTime lastWriteTime = File.GetLastWriteTimeUtc(fileName);

            if (node != null)
            {
                ProfileResponse? profile = JsonSerializer.Deserialize<ProfileResponse>(node["profile"]);
                string? persistedToken = (string?)node["token"];
                string? refreshToken = (string?)node["refresh_token"];
                var expiration = (DateTime?)node["expiration"];

                if (profile != null
                    && persistedToken != null
                    && refreshToken != null
                    && expiration.HasValue)
                {
                    return new AuthenticatedUser(
                        new Profile(profile, this),
                        new AuthResponse
                        {
                            Expiration = expiration.Value,
                            RefreshToken = refreshToken,
                            Token = persistedToken,
                        },
                        this,
                        lastWriteTime);
                }
            }
        }

        return null;
    }

    private static int GetRandomUnusedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static HttpListener StartListener(string redirectUri)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();
        return listener;
    }

    private static async Task<string?> GetResponseFromListener(HttpListener listener, CancellationToken ct)
    {
        HttpListenerContext context;

        using (ct.Register(listener.Stop))
        {
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                ct.ThrowIfCancellationRequested();
                // Next line will never be reached because cancellation will always have been requested in this catch block.
                // But it's required to satisfy compiler.
                throw new InvalidOperationException();
            }
        }

        string? code = context.Request.QueryString.Get("code");

        // Write a "close" response.
        using (Stream input = ReadClosePageResponse())
        {
            context.Response.ContentLength64 = input.Length;
            context.Response.SendChunked = false;
            context.Response.KeepAlive = false;
            context.Response.ContentType = MediaTypeNames.Text.Html;
            using (Stream output = context.Response.OutputStream)
            {
                await input.CopyToAsync(output, ct).ConfigureAwait(false);
                await output.FlushAsync(ct).ConfigureAwait(false);
            }

            context.Response.Close();
        }

        return code;
    }

    private static Stream ReadClosePageResponse()
    {
        Stream? stream =
            typeof(BeutlApiApplication).Assembly.GetManifestResourceStream("Beutl.Api.Resources.index.html");

        return stream ?? throw new Exception("Embedded resource not found.");
    }
}

public sealed class AssetMetadataJson
{
    [JsonPropertyName("id")] public required string Id { get; init; }

    [JsonPropertyName("os")] public required string OS { get; init; }

    [JsonPropertyName("arch")] public required string Arch { get; init; }

    [JsonPropertyName("version")] public required string Version { get; init; }

    [JsonPropertyName("standalone")] public required string Standalone { get; init; }

    // Metadata values: zip,debian,installer,app,flatpak.
    // Server query values (see ToServerType): zip,debian,installer,app.
    [JsonPropertyName("type")] public required string Type { get; init; }
}
