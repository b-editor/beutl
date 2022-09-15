using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Beutl.Api.Objects;

using Reactive.Bindings;

namespace Beutl.Api;

public class BeutlClients
{
    //private const string BaseUrl = "https://localhost:7278";
    private const string BaseUrl = "https://beutl.beditor.net";
    private readonly HttpClient _httpClient;
    private readonly ReactivePropertySlim<AuthorizedUser?> _authorizedUser = new();

    public BeutlClients(HttpClient httpClient)
    {
        _httpClient = httpClient;
        PackageResources = new PackageResourcesClient(httpClient) { BaseUrl = BaseUrl };
        Packages = new PackagesClient(httpClient) { BaseUrl = BaseUrl };
        ReleaseResources = new ReleaseResourcesClient(httpClient) { BaseUrl = BaseUrl };
        Releases = new ReleasesClient(httpClient) { BaseUrl = BaseUrl };
        Users = new UsersClient(httpClient) { BaseUrl = BaseUrl };
        Account = new AccountClient(httpClient) { BaseUrl = BaseUrl };
    }

    public PackageResourcesClient PackageResources { get; }

    public PackagesClient Packages { get; }

    public ReleaseResourcesClient ReleaseResources { get; }

    public ReleasesClient Releases { get; }

    public UsersClient Users { get; }

    public AccountClient Account { get; }

    public IReadOnlyReactiveProperty<AuthorizedUser?> AuthorizedUser => _authorizedUser;

    public async Task<AuthorizedUser> SignInAsync(CancellationToken cancellationToken)
    {
        AuthenticationHeaderValue? defaultAuth = _httpClient.DefaultRequestHeaders.Authorization;

        try
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

            return _authorizedUser.Value = new AuthorizedUser(profile, authResponse, this, _httpClient);
        }
        finally
        {
            _httpClient.DefaultRequestHeaders.Authorization = defaultAuth;
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
                context = await listener.GetContextAsync();
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
        Stream? stream = typeof(BeutlClients).Assembly.GetManifestResourceStream("Beutl.Api.Resources.index.html");

        if (stream == null)
        {
            throw new Exception("Embedded resource not found.");
        }

        return stream;
    }
}
