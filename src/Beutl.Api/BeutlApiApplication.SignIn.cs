using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Net.Sockets;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Activity = System.Diagnostics.Activity;

namespace Beutl.Api;

public partial class BeutlApiApplication
{
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
        AuthenticatedUser user = await RunInteractiveAuthenticationAsync(async authenticationToken =>
        {
            string continueUri = $"http://localhost:{GetRandomUnusedPort()}/__/auth/handler";
            CreateAuthUriResponse authUriRes = await Account.CreateAuthUri(
                new CreateAuthUriRequest { ContinueUri = continueUri },
                authenticationToken);
            using HttpListener listener = StartListener($"{continueUri}/");
            activity?.AddEvent(new("Started_Listener"));

            string uri =
                $"{BaseUrl}/api/v2/identity/signInWith?provider={provider}&returnUrl={Uri.EscapeDataString(authUriRes.AuthUri)}";

            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true, Verb = "open" });

            string? code = await GetResponseFromListener(listener, authenticationToken);
            activity?.AddEvent(new("Received_Code"));
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new Exception("The returned code was empty.");
            }

            AuthResponse authResponse = await Account.Exchange(
                new ExchangeRequest { Code = code, SessionId = authUriRes.SessionId },
                authenticationToken);
            activity?.AddEvent(new("Done_CodeToJwtAsync"));

            ProfileResponse profileResponse = await Users.GetSelf(
                $"Bearer {authResponse.Token}",
                authenticationToken);
            var profile = new Profile(profileResponse, this);
            return new AuthenticatedUser(profile, authResponse, this, DateTime.UtcNow);
        }, token);
        activity?.AddEvent(new("Saved_User"));
        return user;
    }

    public async Task<AuthenticatedUser> SignInAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource lifetimeCts = CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = lifetimeCts.Token;
        using Activity? activity = ActivitySource.StartActivity("SignInAsync", ActivityKind.Client);
        AuthenticatedUser user = await RunInteractiveAuthenticationAsync(async authenticationToken =>
        {
            string continueUri = $"http://localhost:{GetRandomUnusedPort()}/__/auth/handler";
            CreateAuthUriResponse authUriRes = await Account.CreateAuthUri(
                new CreateAuthUriRequest { ContinueUri = continueUri },
                authenticationToken);
            using HttpListener listener = StartListener($"{continueUri}/");
            activity?.AddEvent(new("Started_Listener"));

            string uri = $"{BaseUrl}/account/signIn?returnUrl={Uri.EscapeDataString(authUriRes.AuthUri)}";

            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true, Verb = "open" });

            string? code = await GetResponseFromListener(listener, authenticationToken);
            activity?.AddEvent(new("Received_Code"));
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new Exception("The returned code was empty.");
            }

            AuthResponse authResponse = await Account.Exchange(
                new ExchangeRequest { Code = code, SessionId = authUriRes.SessionId },
                authenticationToken);
            activity?.AddEvent(new("Done_CodeToJwtAsync"));

            ProfileResponse profileResponse = await Users.GetSelf(
                $"Bearer {authResponse.Token}",
                authenticationToken);
            var profile = new Profile(profileResponse, this);
            return new AuthenticatedUser(profile, authResponse, this, DateTime.UtcNow);
        }, token);
        activity?.AddEvent(new("Saved_User"));
        return user;
    }

    internal async Task<AuthenticatedUser> RunInteractiveAuthenticationAsync(
        Func<CancellationToken, Task<AuthenticatedUser>> authenticate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authenticate);
        cancellationToken.ThrowIfCancellationRequested();
        long authenticationAttempt = BeginAuthenticationAttempt();

        AuthenticatedUser user = await authenticate(cancellationToken);
        ArgumentNullException.ThrowIfNull(user);
        using (await Lock.LockAsync(cancellationToken))
        {
            PersistAndCommitAuthenticatedUser(user, authenticationAttempt, cancellationToken);
        }
        return user;
    }

    private void PersistAndCommitAuthenticatedUser(
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

            _persistAuthenticatedUser(user);
            _authenticatedUser.Value = user;
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", user.Token);
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
