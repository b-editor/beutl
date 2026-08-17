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
    private readonly Dictionary<Type, Lazy<object>> _services = [];
    private readonly object _disposeGate = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private static readonly ILogger s_logger = Log.CreateLogger<BeutlApiApplication>();
    private volatile bool _disposed;
    private Task? _disposeTask;
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
        httpClient.BaseAddress = new Uri(BaseUrl);
        App = RestService.For<IAppClient>(httpClient);
        Packages = RestService.For<IPackagesClient>(httpClient);
        Releases = RestService.For<IReleasesClient>(httpClient);
        Files = RestService.For<IFilesClient>(httpClient);
        Users = RestService.For<IUsersClient>(httpClient);
        Account = RestService.For<IAccountClient>(httpClient);
        Discover = RestService.For<IDiscoverClient>(httpClient);
        Library = RestService.For<ILibraryClient>(httpClient);

        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        string culture = viewConfig.UICulture.Name;
        if (!string.IsNullOrWhiteSpace(culture))
        {
            httpClient.DefaultRequestHeaders.AcceptLanguage.Clear();
            httpClient.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(culture));
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

    public IAppClient App { get; }

    public MyAsyncLock Lock { get; } = new();

    public IReadOnlyReactiveProperty<AuthenticatedUser?> AuthenticatedUser => _authenticatedUser;

    public bool IsDisposed => _disposed;

    // 更新があるかどうかをチェックします
    // このアプリケーションがアセットメタデータを持っている場合は、AppUpdateResponseを返します
    // そうでない場合は、CheckForUpdatesResponseを返します
    // The return value is a tuple; the applicable side is set and the other is null.
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

    // The server's /api/v3/app/updates endpoint only accepts zip/debian/installer/app.
    // Flatpak bundles are built from the standalone zip, so report them as zip.
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

    public T? TryGetResource<T>()
        where T : class, IBeutlApiResource
    {
        lock (_disposeGate)
        {
            if (_disposed)
                return null;

            if (_services.TryGetValue(typeof(T), out Lazy<object>? lazy) && lazy.IsValueCreated)
            {
                return (T)lazy.Value;
            }

            foreach (KeyValuePair<Type, Lazy<object>> item in _services)
            {
                if (item.Key.IsAssignableTo(typeof(T)) && item.Value.IsValueCreated)
                {
                    return (T)item.Value.Value;
                }
            }

            return null;
        }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? proxy = null;
        Task disposeTask;
        lock (_disposeGate)
        {
            // Publish a completion proxy before cancellation fires: DisposeCoreAsync runs
            // synchronously through _lifetimeCts.Cancel(), and a re-entrant callback must
            // observe the original teardown instead of starting a second pipeline.
            if (_disposeTask == null)
            {
                proxy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _disposeTask = proxy.Task;
            }
            disposeTask = _disposeTask;
        }

        // Start the teardown after releasing the lock so cancellation callbacks that
        // re-enter disposal or other resource operations do not deadlock against it.
        if (proxy != null)
        {
            _ = RunDisposeCoreAsync(proxy);
        }

        return new ValueTask(disposeTask);
    }

    private async Task RunDisposeCoreAsync(TaskCompletionSource proxy)
    {
        try
        {
            await DisposeCoreAsync().ConfigureAwait(false);
            proxy.TrySetResult();
        }
        catch (Exception ex)
        {
            proxy.TrySetException(ex);
        }
    }

    protected virtual async Task DisposeCoreAsync()
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

        Exception? cancellationFailure = null;
        try
        {
            _lifetimeCts.Cancel();
        }
        catch (Exception ex)
        {
            cancellationFailure = ex;
        }

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

        _lifetimeCts.Dispose();
        ActivitySource.Dispose();

        if (cancellationFailure != null)
        {
            throw cancellationFailure;
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
        Register(() => new PackageInstaller(
            new HttpClient(),
            ownsHttpClient: true,
            GetResource<InstalledPackageRepository>(),
            this));
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

    internal CancellationTokenSource CreateLifetimeLinkedTokenSource(CancellationToken cancellationToken)
    {
        lock (_disposeGate)
        {
            // A canceled caller token must surface as cancellation, not as an
            // ObjectDisposedException, so nested calls started during disposal can
            // observe the shutdown as normal cancellation.
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            return CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCts.Token);
        }
    }

    public void SignOut(bool deleteFile = true)
    {
        _authenticatedUser.Value = null;
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
        using (Activity? activity = ActivitySource.StartActivity("SignInExternalAsync", ActivityKind.Client))
        {
            string continueUri = $"http://localhost:{GetRandomUnusedPort()}/__/auth/handler";
            CreateAuthUriResponse authUriRes = await Account.CreateAuthUri(new CreateAuthUriRequest
            {
                ContinueUri = continueUri
            }, token);
            token.ThrowIfCancellationRequested();
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

            AuthResponse authResponse = await Account.Exchange(new ExchangeRequest
            {
                Code = code,
                SessionId = authUriRes.SessionId
            }, token);
            activity?.AddEvent(new("Done_CodeToJwtAsync"));

            // Serialize only the authentication state transition; the OAuth wait above must
            // not hold the application-wide lock.
            using (await Lock.LockAsync(token))
            {
                activity?.AddEvent(new("Entered_AsyncLock"));
                AuthenticatedUser user = await CompleteSignInAsync(authResponse, token);
                activity?.AddEvent(new("Saved_User"));
                return user;
            }
        }
    }

    public async Task<AuthenticatedUser> SignInAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource lifetimeCts = CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = lifetimeCts.Token;
        using (Activity? activity = ActivitySource.StartActivity("SignInAsync", ActivityKind.Client))
        {
            using (await Lock.LockAsync(token))
            {
                activity?.AddEvent(new("Entered_AsyncLock"));
                string continueUri = $"http://localhost:{GetRandomUnusedPort()}/__/auth/handler";
                CreateAuthUriResponse authUriRes = await Account.CreateAuthUri(new CreateAuthUriRequest
                {
                    ContinueUri = continueUri
                }, token);
                token.ThrowIfCancellationRequested();
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

                AuthResponse authResponse = await Account.Exchange(new ExchangeRequest
                {
                    Code = code,
                    SessionId = authUriRes.SessionId
                }, token);
                activity?.AddEvent(new("Done_CodeToJwtAsync"));

                AuthenticatedUser user = await CompleteSignInAsync(authResponse, token);
                activity?.AddEvent(new("Saved_User"));
                return user;
            }
        }
    }

    internal async Task<AuthenticatedUser> CompleteSignInAsync(
        AuthResponse authResponse,
        CancellationToken cancellationToken)
    {
        string? previousAuthorization = _httpClient.DefaultRequestHeaders.Authorization?.ToString();
        AuthenticatedUser? previousUser = _authenticatedUser.Value;
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", authResponse.Token);
        AuthenticatedUser? attemptedUser = null;
        try
        {
            ProfileResponse profileResponse = await Users.GetSelf(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var profile = new Profile(profileResponse, this);

            attemptedUser = new AuthenticatedUser(profile, authResponse, this, _httpClient, DateTime.UtcNow);
            _authenticatedUser.Value = attemptedUser;
            SaveUser();
            return _authenticatedUser.Value;
        }
        catch
        {
            // Only roll back state still owned by this failing attempt: a SignOut or a
            // later successful sign-in may have replaced the user while GetSelf was pending,
            // and must not be overwritten by the stale snapshot.
            if (ReferenceEquals(_authenticatedUser.Value, attemptedUser)
                || (attemptedUser == null && ReferenceEquals(_authenticatedUser.Value, previousUser)))
            {
                _authenticatedUser.Value = previousUser;
                if (previousAuthorization != null)
                {
                    _httpClient.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(previousAuthorization);
                }
                else
                {
                    _httpClient.DefaultRequestHeaders.Authorization = null;
                }
            }

            throw;
        }
    }

    public static void OpenAccountSettings()
    {
        Process.Start(new ProcessStartInfo($"{BaseUrl}/account/manage") { UseShellExecute = true, Verb = "open" });
    }

    public void SaveUser()
    {
        if (_authenticatedUser.Value is { } user)
        {
            string fileName = Path.Combine(Helper.AppRoot, UserFileName);
            using (FileStream stream = File.Create(fileName))
            {
                var obj = new JsonObject
                {
                    ["token"] = user.Token,
                    ["refresh_token"] = user.RefreshToken,
                    ["expiration"] = user.Expiration,
                    ["profile"] = JsonSerializer.SerializeToNode(user.Profile.Response.Value),
                };

                using var writer = new Utf8JsonWriter(stream);
                obj.WriteTo(writer);
            }

            user._writeTime = File.GetLastWriteTimeUtc(fileName);
        }
    }

    public async Task RestoreUserAsync(Activity? activity, CancellationToken cancellationToken)
    {
        using CancellationTokenSource lifetimeCts = CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = lifetimeCts.Token;
        using (await Lock.LockAsync(token))
        {
            activity?.AddEvent(new("Entered_AsyncLock"));

            string? previousAuthorization = _httpClient.DefaultRequestHeaders.Authorization?.ToString();
            AuthenticatedUser? previousUser = _authenticatedUser.Value;
            AuthenticatedUser? user = await ReadUserAsync(token);
            if (user != null)
            {
                try
                {
                    await user.RefreshAsync(token);

                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
                    await user.Profile.RefreshAsync(token, true);
                    token.ThrowIfCancellationRequested();
                    _authenticatedUser.Value = user;
                    SaveUser();
                }
                catch
                {
                    _authenticatedUser.Value = previousUser;
                    if (previousAuthorization != null)
                    {
                        _httpClient.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(previousAuthorization);
                    }
                    else
                    {
                        _httpClient.DefaultRequestHeaders.Authorization = null;
                    }

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
                        new AuthResponse { Expiration = expiration.Value, RefreshToken = refreshToken, Token = persistedToken },
                        this,
                        _httpClient,
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
