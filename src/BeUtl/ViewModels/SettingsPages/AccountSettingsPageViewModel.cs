using Beutl.Api;
using Beutl.Api.Objects;

using BeUtl.Controls.Navigation;
using BeUtl.ViewModels.Dialogs;

using Reactive.Bindings;

namespace BeUtl.ViewModels.SettingsPages;

public sealed class AccountSettingsPageViewModel : PageContext, IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly BeutlClients _clients;
    private readonly ReactivePropertySlim<CancellationTokenSource?> _cts = new();

    public AccountSettingsPageViewModel(BeutlClients clients)
    {
        _clients = clients;

        SigningIn = _cts.Select(x => x != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        SignIn = new(SigningIn.Select(x => !x));
        SignInWithGoogle = new(SigningIn.Select(x => !x));
        SignInWithGitHub = new(SigningIn.Select(x => !x));

        SignIn.Subscribe(async () => await SignInCore(null))
            .DisposeWith(_disposables);
        SignInWithGoogle.Subscribe(async () => await SignInCore("Google"))
            .DisposeWith(_disposables);
        SignInWithGitHub.Subscribe(async () => await SignInCore("GitHub"))
            .DisposeWith(_disposables);

        Cancel = new(SigningIn);
        Cancel.Subscribe(() => _cts.Value!.Cancel());

        SignedIn = clients.AuthorizedUser.Select(x => x != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        ProfileImage = _clients.AuthorizedUser
            .SelectMany(x => x?.Profile?.AvatarUrl ?? Observable.Return<string?>(null))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Name = _clients.AuthorizedUser
            .Select(x => x?.Profile?.Name)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        DisplayName = _clients.AuthorizedUser
            .SelectMany(x => x?.Profile?.DisplayName ?? Observable.Return<string?>(null))
            .Zip(Name, (x, y) => string.IsNullOrEmpty(x) ? y : x)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        SignOut = new ReactiveCommand(SignedIn);
        SignOut.Subscribe(() => _clients.SignOut()).DisposeWith(_disposables);

        OpenAccountSettings = new();
        OpenAccountSettings.Subscribe(() => _clients.OpenAccountSettings());

        Refresh = new AsyncReactiveCommand(IsLoading.Select(x => !x));
        Refresh.Subscribe(async () =>
        {
            try
            {
                IsLoading.Value = true;
                if (_clients.AuthorizedUser.Value is { } user)
                {
                    await user.RefreshAsync();
                    await user.Profile.RefreshAsync();
                }
            }
            catch
            {
                // Todo: エラー説明
            }
            finally
            {
                IsLoading.Value = false;
            }
        });
    }

    public AsyncReactiveCommand SignIn { get; }

    public AsyncReactiveCommand SignInWithGoogle { get; }

    public AsyncReactiveCommand SignInWithGitHub { get; }

    public ReactiveCommand Cancel { get; }

    public ReadOnlyReactivePropertySlim<bool> SigningIn { get; }

    public ReactivePropertySlim<string> Error { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> SignedIn { get; }

    public ReadOnlyReactivePropertySlim<string?> ProfileImage { get; }

    public ReadOnlyReactivePropertySlim<string?> Name { get; }

    public ReadOnlyReactivePropertySlim<string?> DisplayName { get; }

    public ReactiveCommand SignOut { get; }

    public ReactiveCommand OpenAccountSettings { get; }

    public ReactivePropertySlim<bool> IsLoading { get; } = new();

    public AsyncReactiveCommand Refresh { get; }

    public void Dispose()
    {
        _disposables.Dispose();
    }

    public SelectImageAssetViewModel CreateSelectAvatarImage()
    {
        return new SelectImageAssetViewModel(_clients.AuthorizedUser.Value!);
    }

    public async Task UpdateAvatarImage(Asset asset)
    {
        if (_clients.AuthorizedUser.Value is { } user)
        {
            await user.RefreshAsync();
            await asset.UpdateAsync(true);
            await user.Profile.UpdateAsync(avatarId: asset.Id);
        }
    }

    private async Task SignInCore(string? provider = null)
    {
        try
        {
            _cts.Value = new CancellationTokenSource();
            AuthorizedUser? user = provider switch
            {
                "Google" => await _clients.SignInWithGoogleAsync(_cts.Value.Token),
                "GitHub" => await _clients.SignInWithGitHubAsync(_cts.Value.Token),
                _ => await _clients.SignInAsync(_cts.Value.Token),
            };
        }
        catch (BeutlApiException<ApiErrorResponse>)
        {
            // Todo: エラー説明
            Error.Value = S.Warning.APIErrorOccurred;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            Error.Value = S.Warning.AnUnexpectedErrorHasOccurred;
        }
        finally
        {
            _cts.Value = null;
        }
    }
}
