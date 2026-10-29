using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Beutl.Services.VideoUpload;
using Beutl.Testing.Headless;
using Beutl.Views.Tools;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Http;
using Google.Apis.YouTube.v3;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class YouTubeSignInTests
{
    private const string ClientJson = """{"installed":{"client_id":"desktop-client","client_secret":"test-secret"}}""";

    [AvaloniaTest]
    public async Task Sign_in_uses_pkce_shares_the_account_and_restores_and_refreshes_without_a_browser()
    {
        using var fixture = new Fixture();
        using var first = new YouTubeUploadContext(fixture.Service);
        using var second = new YouTubeUploadContext(fixture.Service);
        await first.SignInAsync();
        Assert.That(first.IsSignedIn.Value && second.IsSignedIn.Value, Is.True);
        Assert.That(fixture.Receiver.Calls, Is.EqualTo(1));
        var request = (GoogleAuthorizationCodeRequestUrl)fixture.Receiver.Request!;
        Assert.That(request.Scope, Is.EqualTo(YouTubeService.Scope.YoutubeUpload));
        Assert.That(request.AccessType, Is.EqualTo("offline"));
        Assert.That(request.Prompt, Does.Contain("select_account"));
        Assert.That(request.CodeChallengeMethod, Is.EqualTo("S256"));
        string verifier = fixture.Requests.Single()["code_verifier"]!;
        string challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.That(request.CodeChallenge, Is.EqualTo(challenge));
        Assert.That(request.State, Has.Length.EqualTo(64));
        var metadata = new JsonObject();
        first.WriteToJson(metadata);
        Assert.That(metadata.ToJsonString(), Does.Not.Contain("test-secret").And.Not.Contain("refresh-token"));
        first.Dispose();
        Assert.That(second.IsSignedIn.Value, Is.True, "Closing an output profile must not sign out other profiles.");

        UserCredential original = fixture.Service.Credential.Value!;
        original.Token.IssuedUtc = DateTime.UtcNow.AddHours(-2);
        await original.Flow.DataStore.StoreAsync(original.UserId, original.Token);
        if (!OperatingSystem.IsWindows())
            Assert.That(File.GetUnixFileMode(fixture.CredentialPath),
                Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        var restored = fixture.CreateService();
        await restored.InitializeAsync();
        Assert.That(restored.Credential.Value, Is.Not.Null);
        Assert.That(fixture.Requests.Last()["grant_type"], Is.EqualTo("refresh_token"));
        Assert.That(fixture.Receiver.Calls, Is.EqualTo(1), "Restoration must never open a browser.");

        UserCredential oldCredential = restored.Credential.Value!;
        restored.SignOut();
        await oldCredential.Flow.DataStore.StoreAsync(oldCredential.UserId, oldCredential.Token);
        Assert.That(File.Exists(fixture.CredentialPath), Is.False, "A late refresh must not undo sign-out.");
        await restored.SignInAsync();
        Assert.That(fixture.Receiver.Calls, Is.EqualTo(2));
        Assert.That(restored.Credential.Value, Is.Not.Null);
        restored.SignOut();
    }

    [AvaloniaTest]
    public async Task Revoked_saved_access_requires_sign_in_and_can_be_reconnected()
    {
        using var fixture = new Fixture();
        await fixture.Service.SignInAsync();
        UserCredential credential = fixture.Service.Credential.Value!;
        credential.Token.IssuedUtc = DateTime.UtcNow.AddHours(-2);
        await credential.Flow.DataStore.StoreAsync(credential.UserId, credential.Token);
        fixture.RejectRefresh = true;
        var restored = fixture.CreateService();
        await restored.InitializeAsync();
        Assert.That(restored.Credential.Value, Is.Null);
        Assert.That(restored.Message.Value, Is.EqualTo(YouTubeSignInMessage.Expired));
        Assert.That(File.Exists(fixture.CredentialPath), Is.False);
        Assert.That(fixture.Receiver.Calls, Is.EqualTo(1));
        fixture.RejectRefresh = false;
        await restored.SignInAsync();
        Assert.That(restored.Credential.Value, Is.Not.Null);
        restored.SignOut();
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Cancellation_or_sign_out_during_authorization_cannot_save_a_late_response(bool signOut)
    {
        using var fixture = new Fixture();
        var response = new TaskCompletionSource<AuthorizationCodeResponseUrl>();
        fixture.Receiver.Receive = (_, _) => response.Task;
        Task signingIn = fixture.Service.SignInAsync();
        Assert.That(fixture.Service.IsBusy.Value, Is.True);
        if (signOut) fixture.Service.SignOut();
        else fixture.Service.Cancel();
        response.SetResult(new AuthorizationCodeResponseUrl { Code = "code", State = fixture.Receiver.Request!.State });
        await signingIn;
        Assert.That(fixture.Service.Credential.Value, Is.Null);
        Assert.That(File.Exists(fixture.CredentialPath), Is.False);
        Assert.That(fixture.Service.IsBusy.Value, Is.False);
        fixture.Receiver.Receive = Receiver.Success;
        await fixture.Service.SignInAsync();
        Assert.That(fixture.Service.Credential.Value, Is.Not.Null);
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Denial_or_mismatched_state_never_reaches_the_token_endpoint(bool badState)
    {
        using var fixture = new Fixture();
        fixture.Receiver.Receive = (request, _) => Task.FromResult(new AuthorizationCodeResponseUrl
        {
            State = badState ? "wrong-state" : request.State,
            Code = badState ? "code" : null,
            Error = badState ? null : "access_denied"
        });
        await fixture.Service.SignInAsync();
        Assert.That(fixture.Service.Credential.Value, Is.Null);
        Assert.That(fixture.Requests, Is.Empty);
        Assert.That(File.Exists(fixture.CredentialPath), Is.False);
        Assert.That(fixture.Service.Message.Value, Is.EqualTo(YouTubeSignInMessage.Failed));
    }

    [AvaloniaTest]
    public async Task Unconfigured_build_can_import_a_desktop_client_once_and_reuse_it()
    {
        using var fixture = new Fixture();
        var service = fixture.CreateService(bundled: false);
        await service.InitializeAsync();
        Assert.That(service.IsConfigured.Value, Is.False);
        Assert.That(service.Message.Value, Is.EqualTo(YouTubeSignInMessage.NotConfigured));
        string source = Path.Combine(fixture.Directory, "client-source.json");
        File.WriteAllText(source, """{"web":{"client_id":"wrong","client_secret":"do-not-display"}}""");
        service.ConfigureClient(source);
        Assert.That(service.IsConfigured.Value, Is.False);
        Assert.That(service.Message.Value, Is.EqualTo(YouTubeSignInMessage.InvalidClient));
        File.WriteAllText(source, ClientJson);
        service.ConfigureClient(source);
        File.Delete(source);
        Assert.That(service.IsConfigured.Value, Is.True);
        await service.SignInAsync();
        Assert.That(service.Credential.Value, Is.Not.Null);
        var restored = fixture.CreateService(bundled: false);
        await restored.InitializeAsync();
        Assert.That(restored.Credential.Value, Is.Not.Null);
        Assert.That(fixture.Receiver.Calls, Is.EqualTo(1));
        restored.SignOut();
    }

    [AvaloniaTest]
    [TestCase("en")]
    [TestCase("ja")]
    public async Task Sign_in_progress_cancel_and_sign_out_fit_a_narrow_panel(string language)
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo(language);
        try
        {
            using var fixture = new Fixture();
            using var context = new YouTubeUploadContext(fixture.Service);
            var view = new YouTubeUploadSettingsView { DataContext = context };
            using var host = new ControlHost(new Window
            {
                Width = 340,
                Height = 800,
                RequestedThemeVariant = language == "ja" ? ThemeVariant.Dark : ThemeVariant.Light,
                Content = new ScrollViewer { Content = view }
            });
            HeadlessTestHelpers.Render();
            Button signIn = view.FindControl<Button>("ConnectButton")!;
            Assert.That(signIn.IsEffectivelyEnabled, Is.True);
            Assert.That(signIn.Content, Is.EqualTo(Beutl.Language.SettingsStrings.SignIn));
            var response = new TaskCompletionSource<AuthorizationCodeResponseUrl>();
            fixture.Receiver.Receive = (_, _) => response.Task;
            Task pending = context.SignInAsync();
            HeadlessTestHelpers.Render();
            Assert.That(signIn.IsVisible, Is.False);
            Assert.That(view.FindControl<Button>("CancelSignInButton")!.IsEffectivelyVisible, Is.True);
            HeadlessCapture.SaveIfRequested(view, nameof(YouTubeSignInTests), $"waiting-{language}");
            response.SetResult(new AuthorizationCodeResponseUrl { Code = "code", State = fixture.Receiver.Request!.State });
            await pending;
            HeadlessTestHelpers.Render();
            Assert.That(context.IsSignedIn.Value, Is.True);
            Button signOut = view.FindControl<Button>("SignOutButton")!;
            Assert.That(signOut.IsEffectivelyVisible, Is.True);
            HeadlessCapture.SaveIfRequested(view, nameof(YouTubeSignInTests), $"connected-{language}");
            signOut.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Render();
            Assert.That(context.IsSignedIn.Value, Is.False);
            Assert.That(signIn.IsEffectivelyVisible, Is.True);
            Assert.That(File.Exists(fixture.CredentialPath), Is.False);
        }
        finally { System.Globalization.CultureInfo.CurrentUICulture = previous; }
    }

    private sealed class Fixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "beutl-youtube-sign-in-" + Guid.NewGuid());
        public string CredentialPath => Path.Combine(Directory, "credential.json");
        public Receiver Receiver { get; } = new();
        public List<System.Collections.Specialized.NameValueCollection> Requests { get; } = [];
        public bool RejectRefresh { get; set; }
        public YouTubeSignInService Service { get; }

        public Fixture()
        {
            System.IO.Directory.CreateDirectory(Directory);
            Service = CreateService();
        }

        public YouTubeSignInService CreateService(bool bundled = true) => new(Directory,
            () => bundled ? new MemoryStream(Encoding.UTF8.GetBytes(ClientJson)) : null,
            () => Receiver, new Factory(async (request, token) =>
            {
                Assert.That(request.RequestUri!.Host, Is.EqualTo("oauth2.googleapis.com"));
                var form = System.Web.HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(token));
                Requests.Add(form);
                bool reject = RejectRefresh && form["grant_type"] == "refresh_token";
                return new HttpResponseMessage(reject ? HttpStatusCode.BadRequest : HttpStatusCode.OK)
                {
                    Content = new StringContent(reject ? """{"error":"invalid_grant"}""" :
                        """{"access_token":"access-token","refresh_token":"refresh-token","token_type":"Bearer","expires_in":3600}""",
                        Encoding.UTF8, "application/json")
                };
            }));

        public void Dispose()
        {
            Service.SignOut();
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }

    private sealed class Receiver : ICodeReceiver
    {
        public string RedirectUri => "http://127.0.0.1:12345/authorize/";
        public int Calls { get; private set; }
        public AuthorizationCodeRequestUrl? Request { get; private set; }
        public Func<AuthorizationCodeRequestUrl, CancellationToken, Task<AuthorizationCodeResponseUrl>> Receive { get; set; } = Success;
        public static Task<AuthorizationCodeResponseUrl> Success(AuthorizationCodeRequestUrl request, CancellationToken token)
            => Task.FromResult(new AuthorizationCodeResponseUrl { Code = "code", State = request.State });
        public Task<AuthorizationCodeResponseUrl> ReceiveCodeAsync(AuthorizationCodeRequestUrl url, CancellationToken taskCancellationToken)
        {
            Calls++;
            Request = url;
            return Receive(url, taskCancellationToken);
        }
    }

    private sealed class Factory(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : Google.Apis.Http.HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => new Handler(send);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }
}
