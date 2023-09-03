using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;

using Beutl.Api.Objects;
using Beutl.Api.Services;

using Beutl;
using Beutl.Configuration;

using Reactive.Bindings;
using Nito.AsyncEx;

namespace Beutl.Api;

public class BeutlApiApplication
{
    //private const string BaseUrl = "https://localhost:7278";
    private const string BaseUrl = "https://beutl.beditor.net";
    private readonly HttpClient _httpClient;
    private readonly ReactivePropertySlim<AuthorizedUser?> _authorizedUser = new();
    private readonly Dictionary<Type, Func<object>> _services = new();

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
        httpClient.DefaultRequestHeaders.AcceptLanguage.Clear();
        httpClient.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(viewConfig.UICulture.Name));

        RegisterAll();
    }

    public PackagesClient Packages { get; }

    public ReleasesClient Releases { get; }

    public UsersClient Users { get; }

    public AccountClient Account { get; }

    public AssetsClient Assets { get; }

    public DiscoverClient Discover { get; }

    public LibraryClient Library { get; }

    public AppClient App { get; }

    public AsyncLock Lock { get; } = new();

    public IReadOnlyReactiveProperty<AuthorizedUser?> AuthorizedUser => _authorizedUser;

    public T GetResource<T>()
        where T : IBeutlApiResource
    {
        if (_services.TryGetValue(typeof(T), out Func<object>? func))
        {
            return (T)func();
        }

        foreach (KeyValuePair<Type, Func<object>> item in _services)
        {
            if (item.Key.IsAssignableTo(typeof(T)))
            {
                return (T)item.Value();
            }
        }

        throw new Exception("Resource not found");
    }

    private void RegisterAll()
    {
        Register(() => new DiscoverService(this));
        Register(() => GetResource<PackageManager>().ExtensionProvider);
        Register(() => new InstalledPackageRepository());
        Register(() => new AcceptedLicenseManager());
        Register(() => new PackageChangesQueue());
        Register(() => new LibraryService(this));
        Register(() => new PackageInstaller(new HttpClient(), GetResource<InstalledPackageRepository>()));
        Register(() => new PackageManager(GetResource<InstalledPackageRepository>(), this));
    }

    private void Register<T>(Func<T> factory)
        where T : IBeutlApiResource
    {
        IBeutlApiResource? obj = null;
        _services.Add(typeof(T), () =>
        {
            obj ??= factory();
            return obj;
        });
    }

    public void SignOut()
    {
        _authorizedUser.Value = null;
        string fileName = Path.Combine(Helper.AppRoot, "user.json");
        if (File.Exists(fileName))
        {
            File.Delete(fileName);
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
        string continueUri = $"http://localhost:{GetRandomUnusedPort()}/__/auth/handler";
        CreateAuthUriResponse authUriRes = await Account.CreateAuthUriAsync(new CreateAuthUriRequest(continueUri), cancellationToken);
        using HttpListener listener = StartListener($"{continueUri}/");

        string uri = $"{BaseUrl}/Identity/Account/Login?provider={provider}&returnUrl={authUriRes.Auth_uri}";

        Process.Start(new ProcessStartInfo(uri)
        {
            UseShellExecute = true
        });

        string? code = await GetResponseFromListener(listener, cancellationToken);
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new Exception("The returned code was empty.");
        }

        AuthResponse authResponse = await Account.CodeToJwtAsync(new CodeToJwtRequest(code, authUriRes.Session_id), cancellationToken);

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResponse.Token);
        ProfileResponse profileResponse = await Users.Get2Async(cancellationToken);
        var profile = new Profile(profileResponse, this);

        _authorizedUser.Value = new AuthorizedUser(profile, authResponse, this, _httpClient);
        SaveUser();
        return _authorizedUser.Value;
    }

    public async Task<AuthorizedUser> SignInAsync(CancellationToken cancellationToken)
    {
        using (await Lock.LockAsync(cancellationToken))
        {
            string continueUri = $"http://localhost:{GetRandomUnusedPort()}/__/auth/handler";
            CreateAuthUriResponse authUriRes = await Account.CreateAuthUriAsync(new CreateAuthUriRequest(continueUri), cancellationToken);
            using HttpListener listener = StartListener($"{continueUri}/");

            string uri = $"{BaseUrl}/Identity/Account/Login?returnUrl={authUriRes.Auth_uri}";

            Process.Start(new ProcessStartInfo(uri)
            {
                UseShellExecute = true
            });

            string? code = await GetResponseFromListener(listener, cancellationToken);
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new Exception("The returned code was empty.");
            }

            AuthResponse authResponse = await Account.CodeToJwtAsync(new CodeToJwtRequest(code, authUriRes.Session_id), cancellationToken);

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResponse.Token);
            ProfileResponse profileResponse = await Users.Get2Async(cancellationToken);
            var profile = new Profile(profileResponse, this);

            _authorizedUser.Value = new AuthorizedUser(profile, authResponse, this, _httpClient);
            SaveUser();
            return _authorizedUser.Value;
        }
    }

    public static void OpenAccountSettings()
    {
        Process.Start(new ProcessStartInfo($"{BaseUrl}/Identity/Account/Manage")
        {
            UseShellExecute = true
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
        }
    }

    public async Task RestoreUserAsync()
    {
        using (await Lock.LockAsync())
        {
            string fileName = Path.Combine(Helper.AppRoot, "user.json");
            if (File.Exists(fileName))
            {
                JsonNode? node;
                using (StreamReader reader = File.OpenText(fileName))
                {
                    string json = await reader.ReadToEndAsync();
                    node = JsonNode.Parse(json);
                }

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
                        var user = new AuthorizedUser(new Profile(profile, this), new AuthResponse(expiration.Value, refreshToken, token), this, _httpClient);
                        await user.RefreshAsync();

                        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
                        await user.Profile.RefreshAsync();
                        _authorizedUser.Value = user;
                        SaveUser();
                    }
                }
            }
        }
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

        if (stream == null)
        {
            throw new Exception("Embedded resource not found.");
        }

        return stream;
    }
}
