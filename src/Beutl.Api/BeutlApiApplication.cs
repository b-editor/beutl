using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;

using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Configuration;

using Reactive.Bindings;

namespace Beutl.Api;

public class BeutlApiApplication
{
    //private const string BaseUrl = "https://localhost:44459";
    private const string BaseUrl = "https://beutl.beditor.net";
    private readonly HttpClient _httpClient;
    private readonly ReactivePropertySlim<AuthorizedUser?> _authorizedUser = new();
    private readonly Dictionary<Type, Lazy<object>> _services = [];

    public BeutlApiApplication(HttpClient httpClient)
    {
        _httpClient = httpClient;
        Packages = new PackagesClient(httpClient) { BaseUrl = BaseUrl };
        Releases = new ReleasesClient(httpClient) { BaseUrl = BaseUrl };
        Users = new UsersClient(httpClient) { BaseUrl = BaseUrl };
        Account = new AccountClient(httpClient) { BaseUrl = BaseUrl };
        Assets = new AssetsClient(httpClient) { BaseUrl = BaseUrl };
        Discover = new DiscoverClient(httpClient) { BaseUrl = BaseUrl };
        Library = new LibraryClient(httpClient) { BaseUrl = BaseUrl };
        App = new AppClient(httpClient) { BaseUrl = BaseUrl };

        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        string culture = viewConfig.UICulture.Name;
        if (!string.IsNullOrWhiteSpace(culture))
        {
            httpClient.DefaultRequestHeaders.AcceptLanguage.Clear();
            httpClient.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(culture));
        }

        RegisterAll();
    }

    public ActivitySource ActivitySource { get; } = new("Beutl.Api.Client", GitVersionInformation.SemVer);

    public PackagesClient Packages { get; }

    public ReleasesClient Releases { get; }

    public UsersClient Users { get; }

    public AccountClient Account { get; }

    public AssetsClient Assets { get; }

    public DiscoverClient Discover { get; }

    public LibraryClient Library { get; }

    public AppClient App { get; }

    public MyAsyncLock Lock { get; } = new();

    public IReadOnlyReactiveProperty<AuthorizedUser?> AuthorizedUser => _authorizedUser;

    public T GetResource<T>()
        where T : IBeutlApiResource
    {
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

    private void RegisterAll()
    {
        Register(() => new DiscoverService(this));
        Register(() => ExtensionProvider.Current);
        Register(() => new InstalledPackageRepository());
        Register(() => new AcceptedLicenseManager());
        Register(() => new PackageChangesQueue());
        Register(() => new LibraryService(this));
        Register(() => new PackageInstaller(new HttpClient(), GetResource<InstalledPackageRepository>()));
        Register(() => new PackageManager(GetResource<InstalledPackageRepository>(), GetResource<ExtensionProvider>(), this));
    }

    private void Register<T>(Func<T> factory)
        where T : IBeutlApiResource
    {
        _services.Add(typeof(T), new Lazy<object>(() => factory()));
    }

    public void SignOut(bool deleteFile = true)
    {
        _authorizedUser.Value = null;
        if (deleteFile)
        {
            string fileName = Path.Combine(Helper.AppRoot, "user.json");
            if (File.Exists(fileName))
            {
                File.Delete(fileName);
            }
        }
    }

    public Task<AuthorizedUser> SignInWithGoogleAsync(CancellationToken cancellationToken)
    {
        return SignInExternalAsync("Google", cancellationToken);
    }

    public Task<AuthorizedUser> SignInWithGitHubAsync(CancellationToken cancellationToken)
    {
        return SignInExternalAsync("GitHub", cancellationToken);
    }

    private async Task<AuthorizedUser> SignInExternalAsync(string provider, CancellationToken cancellationToken)
    {
        using (Activity? activity = ActivitySource.StartActivity("SignInExternalAsync", ActivityKind.Client))
        {
            string continueUri = $"http://localhost:{GetRandomUnusedPort()}/__/auth/handler";
            CreateAuthUriResponse authUriRes = await Account.CreateAuthUriAsync(new CreateAuthUriRequest(continueUri), cancellationToken);
            using HttpListener listener = StartListener($"{continueUri}/");
            activity?.AddEvent(new("Started_Listener"));

            string uri = $"{BaseUrl}/api/v2/identity/signInWith?provider={provider}&returnUrl={Uri.EscapeDataString(authUriRes.Auth_uri)}";

            Process.Start(new ProcessStartInfo(uri)
            {
                UseShellExecute = true,
                Verb = "open"
            });

            string? code = await GetResponseFromListener(listener, cancellationToken);
            activity?.AddEvent(new("Received_Code"));
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new Exception("The returned code was empty.");
            }

            AuthResponse authResponse = await Account.CodeToJwtAsync(new CodeToJwtRequest(code, authUriRes.Session_id), cancellationToken);
            activity?.AddEvent(new("Done_CodeToJwtAsync"));

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResponse.Token);
            ProfileResponse profileResponse = await Users.Get2Async(cancellationToken);
            var profile = new Profile(profileResponse, this);

            _authorizedUser.Value = new AuthorizedUser(profile, authResponse, this, _httpClient, DateTime.UtcNow);
            SaveUser();
            activity?.AddEvent(new("Saved_User"));
            return _authorizedUser.Value;
        }
    }

    public async Task<AuthorizedUser> SignInAsync(CancellationToken cancellationToken)
    {
        using (Activity? activity = ActivitySource.StartActivity("SignInAsync", ActivityKind.Client))
        {
            using (await Lock.LockAsync(cancellationToken))
            {
                activity?.AddEvent(new("Entered_AsyncLock"));
                string continueUri = $"http://localhost:{GetRandomUnusedPort()}/__/auth/handler";
                CreateAuthUriResponse authUriRes = await Account.CreateAuthUriAsync(new CreateAuthUriRequest(continueUri), cancellationToken);
                using HttpListener listener = StartListener($"{continueUri}/");
                activity?.AddEvent(new("Started_Listener"));

                string uri = $"{BaseUrl}/account/signIn?returnUrl={Uri.EscapeDataString(authUriRes.Auth_uri)}";

                Process.Start(new ProcessStartInfo(uri)
                {
                    UseShellExecute = true,
                    Verb = "open"
                });

                string? code = await GetResponseFromListener(listener, cancellationToken);
                activity?.AddEvent(new("Received_Code"));
                if (string.IsNullOrWhiteSpace(code))
                {
                    throw new Exception("The returned code was empty.");
                }

                AuthResponse authResponse = await Account.CodeToJwtAsync(new CodeToJwtRequest(code, authUriRes.Session_id), cancellationToken);
                activity?.AddEvent(new("Done_CodeToJwtAsync"));

                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResponse.Token);
                ProfileResponse profileResponse = await Users.Get2Async(cancellationToken);
                var profile = new Profile(profileResponse, this);

                _authorizedUser.Value = new AuthorizedUser(profile, authResponse, this, _httpClient, DateTime.UtcNow);
                SaveUser();
                activity?.AddEvent(new("Saved_User"));
                return _authorizedUser.Value;
            }
        }
    }

    public static void OpenAccountSettings()
    {
        Process.Start(new ProcessStartInfo($"{BaseUrl}/account/manage")
        {
            UseShellExecute = true,
            Verb = "open"
        });
    }

    public void SaveUser()
    {
        if (_authorizedUser.Value is { } user)
        {
            string fileName = Path.Combine(Helper.AppRoot, "user.json");
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

    public async Task RestoreUserAsync(Activity? activity)
    {
        using (await Lock.LockAsync())
        {
            activity?.AddEvent(new("Entered_AsyncLock"));

            AuthorizedUser? user = await ReadUserAsync();
            if (user != null)
            {
                await user.RefreshAsync();

                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
                await user.Profile.RefreshAsync();
                _authorizedUser.Value = user;
                SaveUser();
            }
        }
    }

    public async ValueTask<AuthorizedUser?> ReadUserAsync()
    {
        string fileName = Path.Combine(Helper.AppRoot, "user.json");
        if (File.Exists(fileName))
        {
            JsonNode? node = JsonNode.Parse(await File.ReadAllTextAsync(fileName));
            DateTime lastWriteTime = File.GetLastWriteTimeUtc(fileName);

            if (node != null)
            {
                ProfileResponse? profile = JsonSerializer.Deserialize<ProfileResponse>(node["profile"]);
                string? token = (string?)node["token"];
                string? refreshToken = (string?)node["refresh_token"];
                var expiration = (DateTimeOffset?)node["expiration"];

                if (profile != null
                    && token != null
                    && refreshToken != null
                    && expiration.HasValue)
                {
                    return new AuthorizedUser(
                        new Profile(profile, this),
                        new AuthResponse(expiration.Value, refreshToken, token),
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
        Stream? stream = typeof(BeutlApiApplication).Assembly.GetManifestResourceStream("Beutl.Api.Resources.index.html");

        return stream ?? throw new Exception("Embedded resource not found.");
    }
}
