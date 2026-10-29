using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.YouTube.v3;
using Reactive.Bindings;

namespace Beutl.Services.VideoUpload;

internal enum YouTubeSignInMessage { None, NotConfigured, Failed, Cancelled, Expired, SignOutFailed, InvalidClient }

internal sealed class YouTubeSignInService
{
    private const string UserId = "beutl-upload";
    private static readonly Lazy<YouTubeSignInService> s_default = new(() => new(
        Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "accounts", "youtube"),
        () => typeof(YouTubeSignInService).Assembly.GetManifestResourceStream("Beutl.YouTubeOAuthClient.json")));
    private readonly string _directory;
    private readonly Func<Stream?> _bundledClient;
    private readonly Func<ICodeReceiver> _receiver;
    private readonly Google.Apis.Http.IHttpClientFactory? _httpClientFactory;
    private Task? _initialization;
    private CancellationTokenSource? _cancellation;
    private YouTubeCredentialStore? _store;

    public static YouTubeSignInService Default => s_default.Value;

    internal YouTubeSignInService(string directory, Func<Stream?>? bundledClient = null,
        Func<ICodeReceiver>? receiver = null, Google.Apis.Http.IHttpClientFactory? httpClientFactory = null)
    {
        _directory = directory;
        _bundledClient = bundledClient ?? (() => null);
        _receiver = receiver ?? (() => new LocalServerCodeReceiver());
        _httpClientFactory = httpClientFactory;
    }

    public ReactivePropertySlim<UserCredential?> Credential { get; } = new();
    public ReactivePropertySlim<bool> IsBusy { get; } = new();
    public ReactivePropertySlim<bool> IsRestoring { get; } = new();
    public ReactivePropertySlim<bool> IsConfigured { get; } = new();
    public ReactivePropertySlim<YouTubeSignInMessage> Message { get; } = new();

    public Task InitializeAsync() => _initialization ??= AuthenticateAsync(interactive: false);

    public Task SignInAsync() => AuthenticateAsync(interactive: true);

    public void Cancel() => _cancellation?.Cancel();

    public void SignOut()
    {
        Cancel();
        Credential.Value = null;
        Message.Value = YouTubeSignInMessage.None;
        try
        {
            if (_store != null) _store.Disable(clear: true);
            else File.Delete(Path.Combine(_directory, "credential.json"));
        }
        catch (Exception) { Message.Value = YouTubeSignInMessage.SignOutFailed; }
    }

    public void ConfigureClient(string path)
    {
        if (IsBusy.Value) return;
        try
        {
            using FileStream input = File.OpenRead(path);
            ClientSecrets client = ReadClient(input);
            Directory.CreateDirectory(_directory);
            new JsonObject
            {
                ["installed"] = new JsonObject
                {
                    ["client_id"] = client.ClientId,
                    ["client_secret"] = client.ClientSecret
                }
            }.JsonSave(Path.Combine(_directory, "client.json"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
            SignOut();
            IsConfigured.Value = true;
        }
        catch (Exception) { Message.Value = YouTubeSignInMessage.InvalidClient; }
    }

    private async Task AuthenticateAsync(bool interactive)
    {
        if (IsBusy.Value || Credential.Value != null) return;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        _cancellation = cancellation;
        IsRestoring.Value = !interactive;
        IsBusy.Value = true;
        Message.Value = YouTubeSignInMessage.None;
        YouTubeCredentialStore? store = null;
        PkceGoogleAuthorizationCodeFlow? flow = null;
        try
        {
            string localClient = Path.Combine(_directory, "client.json");
            using Stream? input = File.Exists(localClient) ? File.OpenRead(localClient) : _bundledClient();
            if (input == null)
            {
                IsConfigured.Value = false;
                Message.Value = YouTubeSignInMessage.NotConfigured;
                return;
            }
            ClientSecrets client = ReadClient(input);
            IsConfigured.Value = true;
            _store?.Disable(clear: false);
            _store = store = new YouTubeCredentialStore(Path.Combine(_directory, "credential.json"), client.ClientId);
            flow = new PkceGoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = client,
                Scopes = [YouTubeService.Scope.YoutubeUpload],
                Prompt = "select_account consent",
                DataStore = store,
                HttpClientFactory = _httpClientFactory
            });
            UserCredential credential;
            if (interactive)
            {
                credential = await new AuthorizationCodeInstalledApp(flow,
                    new StateCodeReceiver(_receiver())).AuthorizeAsync(UserId, cancellation.Token);
            }
            else
            {
                TokenResponse? token = await flow.LoadTokenAsync(UserId, cancellation.Token);
                if (token == null) return;
                credential = new UserCredential(flow, UserId, token);
            }
            await credential.GetAccessTokenForRequestAsync(cancellationToken: cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            store.Commit();
            Credential.Value = credential;
            // The credential also owns the flow while a captured upload is using it.
            flow = null;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Message.Value = YouTubeSignInMessage.Cancelled;
        }
        catch (TokenResponseException ex) when (ex.Error?.Error == "invalid_grant")
        {
            try
            {
                store?.Disable(clear: true);
                Message.Value = YouTubeSignInMessage.Expired;
            }
            catch (Exception) { Message.Value = YouTubeSignInMessage.SignOutFailed; }
        }
        catch (Exception)
        {
            // Never expose credential contents or OAuth responses in dialogs or telemetry.
            Message.Value = YouTubeSignInMessage.Failed;
        }
        finally
        {
            if (Credential.Value == null) store?.Disable(clear: false);
            flow?.Dispose();
            _cancellation = null;
            IsBusy.Value = false;
            IsRestoring.Value = false;
        }
    }

    private static ClientSecrets ReadClient(Stream stream)
    {
        JsonNode? json = JsonNode.Parse(stream);
        if (json?["installed"] is not JsonObject installed
            || installed["client_id"]?.GetValue<string>() is not { Length: > 0 } id
            || installed["client_secret"]?.GetValue<string>() is not { Length: > 0 } secret)
            throw new InvalidDataException();
        return new ClientSecrets { ClientId = id, ClientSecret = secret };
    }

    internal sealed class StateCodeReceiver(ICodeReceiver inner) : ICodeReceiver
    {
        public string RedirectUri => inner.RedirectUri;

        public async Task<AuthorizationCodeResponseUrl> ReceiveCodeAsync(AuthorizationCodeRequestUrl url,
            CancellationToken taskCancellationToken)
        {
            string state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            url.State = state;
            AuthorizationCodeResponseUrl response = await inner.ReceiveCodeAsync(url, taskCancellationToken);
            if (response.State != state) throw new InvalidOperationException("OAuth state mismatch.");
            return response;
        }
    }
}
