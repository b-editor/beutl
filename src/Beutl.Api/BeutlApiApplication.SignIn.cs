using System.Diagnostics;
using System.Net;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
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
            returnUrl => $"{BaseUrl}/api/v2/identity/signInWith?provider={provider}&returnUrl={Uri.EscapeDataString(returnUrl)}",
            cancellationToken);
    }

    public async Task<AuthenticatedUser> SignInAsync(CancellationToken cancellationToken)
    {
        return await SignInWithBrowserAsync(
            "SignInAsync",
            returnUrl => returnUrl,
            cancellationToken);
    }

    // Provider-specific sign-ins return to the same OAuth authorization request.
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
            string continueUri = $"http://127.0.0.1:{LoopbackAuthorizationListener.GetRandomUnusedPort()}/__/auth/handler/";
            NativeAuthorization authorization = NativeOAuthClient.CreateAuthorization(BaseUrl, continueUri);
            using HttpListener listener = LoopbackAuthorizationListener.StartListener(continueUri);
            activity?.AddEvent(new("Started_Listener"));

            string uri = createSignInUri(authorization.Uri.AbsoluteUri);

            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true, Verb = "open" });

            string code = await LoopbackAuthorizationListener.GetResponseFromListener(
                listener, authorization.State, $"{BaseUrl}/api/auth", authenticationToken);
            activity?.AddEvent(new("Received_Code"));
            AuthResponse authResponse = await NativeOAuthClient.ExchangeAsync(_httpClient, authorization, code, authenticationToken);
            activity?.AddEvent(new("Done_CodeToJwtAsync"));

            ProfileResponse profileResponse = await NativeOAuthClient.GetProfileAsync(_httpClient, authResponse, authenticationToken);
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
            CommitAuthenticatedUser(user, authenticationAttempt, persist: true, cancellationToken);
        }
        return user;
    }
}
