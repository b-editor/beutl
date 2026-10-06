using System.Diagnostics;
using System.Net;
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

    private Task<AuthenticatedUser> SignInExternalAsync(string provider, CancellationToken cancellationToken)
    {
        return SignInWithBrowserAsync(
            "SignInExternalAsync",
            returnUrl => $"{BaseUrl}/api/v2/identity/signInWith?provider={provider}&returnUrl={returnUrl}",
            cancellationToken);
    }

    public async Task<AuthenticatedUser> SignInAsync(CancellationToken cancellationToken)
    {
        return await SignInWithBrowserAsync(
            "SignInAsync",
            returnUrl => $"{BaseUrl}/account/signIn?returnUrl={returnUrl}",
            cancellationToken);
    }

    // The browser sign-ins differ only in the page they open; createSignInUri receives the escaped AuthUri
    // that page returns to.
    private async Task<AuthenticatedUser> SignInWithBrowserAsync(
        string activityName,
        Func<string, string> createSignInUri,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource lifetimeCts = CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = lifetimeCts.Token;
        using Activity? activity = ActivitySource.StartActivity(activityName, ActivityKind.Client);
        AuthenticatedUser user = await RunInteractiveAuthenticationAsync(async authenticationToken =>
        {
            string continueUri = $"http://localhost:{LoopbackAuthorizationListener.GetRandomUnusedPort()}/__/auth/handler";
            CreateAuthUriResponse authUriRes = await Account.CreateAuthUri(
                new CreateAuthUriRequest { ContinueUri = continueUri },
                authenticationToken);
            using HttpListener listener = LoopbackAuthorizationListener.StartListener($"{continueUri}/");
            activity?.AddEvent(new("Started_Listener"));

            string uri = createSignInUri(Uri.EscapeDataString(authUriRes.AuthUri));

            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true, Verb = "open" });

            string? code = await LoopbackAuthorizationListener.GetResponseFromListener(listener, authenticationToken);
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
            SetBearerAuthorization(user.Token);
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
}
