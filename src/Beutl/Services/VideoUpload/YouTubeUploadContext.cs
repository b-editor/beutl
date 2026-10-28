using System.Text.Json.Nodes;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Services.VideoUpload;

public sealed class YouTubeUploadContext : IVideoUploadContext
{
    private readonly CompositeDisposable _subscriptions = [];
    private UserCredential? _credential;
    private CancellationTokenSource? _signInCancellation;
    private bool _disposed;

    public YouTubeUploadContext()
    {
        ConnectionStatus = IsSignedIn.CombineLatest(IsSigningIn,
                (signedIn, signingIn) => signingIn ? Strings.VideoUpload_SigningIn
                    : signedIn ? Strings.VideoUpload_SignedIn : Strings.VideoUpload_NotConnected)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_subscriptions);
        IsReady = IsSignedIn.CombineLatest(Audience, Title, Description,
                (signedIn, audience, title, description) => signedIn && audience != null
                    && title.Length <= 100 && !title.Contains('<') && !title.Contains('>')
                    && System.Text.Encoding.UTF8.GetByteCount(description) <= 5000
                    && !description.Contains('<') && !description.Contains('>'))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_subscriptions);
    }

    public ReactivePropertySlim<string> Title { get; } = new("");
    public ReactivePropertySlim<string> Description { get; } = new("");
    public UploadChoice[] Visibilities { get; } =
    [
        new("private", Strings.VideoUpload_Private),
        new("unlisted", Strings.VideoUpload_Unlisted),
        new("public", Strings.VideoUpload_Public)
    ];
    public ReactivePropertySlim<UploadChoice> Visibility { get; } = new(new("private", Strings.VideoUpload_Private));
    public UploadChoice[] Audiences { get; } =
    [
        new("not-for-kids", Strings.VideoUpload_NotForKids),
        new("for-kids", Strings.VideoUpload_ForKids)
    ];
    public ReactivePropertySlim<UploadChoice?> Audience { get; } = new();
    public ReactivePropertySlim<bool> IsSignedIn { get; } = new();
    public ReactivePropertySlim<bool> IsSigningIn { get; } = new();
    public ReactivePropertySlim<string> ConnectionMessage { get; } = new("");
    public ReadOnlyReactivePropertySlim<string?> ConnectionStatus { get; }
    public ReadOnlyReactivePropertySlim<bool> IsReady { get; }
    IReadOnlyReactiveProperty<bool> IVideoUploadContext.IsReady => IsReady;

    // The desktop client's configuration is selected locally. Neither it nor OAuth tokens enter
    // project files, presets or telemetry. The credential lives for this context's session only.
    public async Task SignInAsync(string clientSecretsPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsSigningIn.Value) return;

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        _signInCancellation = cancellation;
        IsSigningIn.Value = true;
        ConnectionMessage.Value = "";
        try
        {
            using FileStream file = File.OpenRead(clientSecretsPath);
            var json = await JsonNode.ParseAsync(file, cancellationToken: cancellation.Token);
            if (json?["installed"] is not JsonObject installed
                || installed["client_id"]?.GetValue<string>() is not { Length: > 0 } clientId
                || installed["client_secret"]?.GetValue<string>() is not { Length: > 0 } clientSecret)
            {
                throw new InvalidOperationException(Strings.VideoUpload_DesktopClientRequired);
            }

            _credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret }
                },
                [YouTubeService.Scope.YoutubeUpload], "beutl-upload", usePkce: true,
                cancellation.Token, new NullDataStore());
            cancellation.Token.ThrowIfCancellationRequested();
            IsSignedIn.Value = true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            _credential = null;
            IsSignedIn.Value = false;
            ConnectionMessage.Value = Strings.VideoUpload_SignInCancelled;
        }
        catch (Exception)
        {
            _credential = null;
            IsSignedIn.Value = false;
            // Credential files and OAuth responses must not end up in error dialogs or logs.
            ConnectionMessage.Value = Strings.VideoUpload_SignInFailed;
        }
        finally
        {
            _signInCancellation = null;
            IsSigningIn.Value = false;
        }
    }

    public void CancelSignIn() => _signInCancellation?.Cancel();

    public void SignOut()
    {
        CancelSignIn();
        _credential = null;
        IsSignedIn.Value = false;
        ConnectionMessage.Value = "";
    }

    public IVideoUploadOperation CreateUpload(string filePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsReady.Value || _credential is not { } credential)
            throw new InvalidOperationException(Strings.VideoUpload_NotReady);

        Video video = CreateVideo(filePath);
        return new YouTubeVideoUploadOperation(filePath, video, () => new YouTubeService(
            new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "Beutl"
            }));
    }

    internal Video CreateVideo(string filePath)
    {
        string title = string.IsNullOrWhiteSpace(Title.Value)
            ? Path.GetFileNameWithoutExtension(filePath) : Title.Value.Trim();
        if (title.Length is 0 or > 100 || title.Contains('<') || title.Contains('>')
            || System.Text.Encoding.UTF8.GetByteCount(Description.Value) > 5000
            || Description.Value.Contains('<') || Description.Value.Contains('>')
            || Audience.Value is null)
            throw new InvalidOperationException(Strings.VideoUpload_InvalidMetadata);

        return new Video
        {
            Snippet = new VideoSnippet { Title = title, Description = Description.Value, CategoryId = "22" },
            Status = new VideoStatus
            {
                PrivacyStatus = Visibility.Value.Value,
                SelfDeclaredMadeForKids = Audience.Value.Value == "for-kids"
            }
        };
    }

    public void WriteToJson(JsonObject json)
    {
        json[nameof(Title)] = Title.Value;
        json[nameof(Description)] = Description.Value;
        json[nameof(Visibility)] = Visibility.Value.Value;
        json[nameof(Audience)] = Audience.Value?.Value;
    }

    public void ReadFromJson(JsonObject json)
    {
        Title.Value = json[nameof(Title)]?.GetValue<string>() ?? "";
        Description.Value = json[nameof(Description)]?.GetValue<string>() ?? "";
        Visibility.Value = Visibilities.FirstOrDefault(x => x.Value == json[nameof(Visibility)]?.GetValue<string>())
            ?? Visibilities[0];
        Audience.Value = Audiences.FirstOrDefault(x => x.Value == json[nameof(Audience)]?.GetValue<string>());
    }

    public void Dispose()
    {
        _disposed = true;
        SignOut();
        _subscriptions.Dispose();
    }

    public sealed record UploadChoice(string Value, string DisplayName);
}
