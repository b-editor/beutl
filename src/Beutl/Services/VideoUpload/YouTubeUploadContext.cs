using System.Text.Json.Nodes;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Services.VideoUpload;

public sealed class YouTubeUploadContext : IVideoUploadContext
{
    private readonly CompositeDisposable _subscriptions = [];
    private UserCredential? _credential;
    private readonly YouTubeSignInService _signIn;
    private bool _disposed;

    public YouTubeUploadContext() : this(YouTubeSignInService.Default) { }

    internal YouTubeUploadContext(YouTubeSignInService signIn)
    {
        _signIn = signIn;
        _signIn.Credential.Subscribe(credential =>
        {
            _credential = credential;
            IsSignedIn.Value = credential != null;
        }).DisposeWith(_subscriptions);
        _signIn.IsBusy.Subscribe(value => IsSigningIn.Value = value).DisposeWith(_subscriptions);
        _signIn.IsConfigured.Subscribe(value => IsConfigured.Value = value).DisposeWith(_subscriptions);
        _signIn.Message.Subscribe(value => ConnectionMessage.Value = value switch
        {
            YouTubeSignInMessage.NotConfigured => Strings.VideoUpload_ClientNotConfigured,
            YouTubeSignInMessage.Failed => Strings.VideoUpload_SignInFailed,
            YouTubeSignInMessage.Cancelled => Strings.VideoUpload_SignInCancelled,
            YouTubeSignInMessage.Expired => Strings.VideoUpload_SignInAgain,
            YouTubeSignInMessage.SignOutFailed => Strings.VideoUpload_SignOutFailed,
            YouTubeSignInMessage.InvalidClient => Strings.VideoUpload_DesktopClientRequired,
            _ => ""
        }).DisposeWith(_subscriptions);
        ConnectionStatus = IsSignedIn.CombineLatest(IsSigningIn, _signIn.IsRestoring,
                (signedIn, signingIn, restoring) => signingIn
                    ? restoring ? Strings.VideoUpload_RestoringConnection : Strings.VideoUpload_SigningIn
                    : signedIn ? Strings.VideoUpload_SignedIn : Strings.VideoUpload_NotConnected)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_subscriptions);
        DescriptionError = Description.Select(description => IsDescriptionValid(description)
                ? null : Strings.VideoUpload_InvalidDescription)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_subscriptions);
        IsReady = IsSignedIn.CombineLatest(Audience, Title, DescriptionError,
                (signedIn, audience, title, descriptionError) => signedIn && audience != null
                    && title.Length <= 100 && !title.Contains('<') && !title.Contains('>')
                    && descriptionError == null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_subscriptions);
        _ = _signIn.InitializeAsync();
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
    public ReactivePropertySlim<bool> IsConfigured { get; } = new();
    public ReactivePropertySlim<string> ConnectionMessage { get; } = new("");
    public ReadOnlyReactivePropertySlim<string?> ConnectionStatus { get; }
    public ReadOnlyReactivePropertySlim<string?> DescriptionError { get; }
    public ReadOnlyReactivePropertySlim<bool> IsReady { get; }
    IReadOnlyReactiveProperty<bool> IVideoUploadContext.IsReady => IsReady;

    public Task SignInAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _signIn.SignInAsync();
    }

    public void ConfigureClient(string path) => _signIn.ConfigureClient(path);

    public void CancelSignIn() => _signIn.Cancel();

    public void SignOut() => _signIn.SignOut();

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
            || !IsDescriptionValid(Description.Value)
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

    private static bool IsDescriptionValid(string description)
        => System.Text.Encoding.UTF8.GetByteCount(description) <= 5000
           && !description.Contains('<') && !description.Contains('>');

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
        _subscriptions.Dispose();
    }

    public sealed record UploadChoice(string Value, string DisplayName);
}
