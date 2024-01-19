using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Services;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.ExtensionsPages;

using OpenTelemetry.Trace;

using Reactive.Bindings;

using Serilog;

namespace Beutl.ViewModels.SettingsPages;

public sealed class AccountSettingsPageViewModel : BasePageViewModel
{
    private readonly ILogger _logger = Log.ForContext<AccountSettingsPageViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly BeutlApiApplication _clients;
    private readonly ReactivePropertySlim<CancellationTokenSource?> _cts = new();

    public AccountSettingsPageViewModel(BeutlApiApplication clients)
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
        OpenAccountSettings.Subscribe(BeutlApiApplication.OpenAccountSettings);

        Refresh = new AsyncReactiveCommand(IsLoading.Select(x => !x));
        Refresh.Subscribe(async () =>
        {
            using (Activity? activity = Telemetry.StartActivity("AccountSettingsPage.Refresh"))
            {
                try
                {
                    IsLoading.Value = true;
                    if (_clients.AuthorizedUser.Value is { } user)
                    {
                        using (await user.Lock.LockAsync())
                        {
                            activity?.AddEvent(new("Entered_AsyncLock"));

                            await user.RefreshAsync();
                            await user.Profile.RefreshAsync();
                        }
                    }
                }
                catch (Exception ex)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    activity?.RecordException(ex);
                    ErrorHandle(ex);
                    _logger.Error(ex, "An unexpected error has occurred.");
                }
                finally
                {
                    IsLoading.Value = false;
                }
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

    public override void Dispose()
    {
        _disposables.Dispose();
    }

    public SelectImageAssetViewModel CreateSelectAvatarImage()
    {
        return new SelectImageAssetViewModel(_clients.AuthorizedUser.Value!);
    }

    public async Task UpdateAvatarImage(Asset asset)
    {
        using (Activity? activity = Telemetry.StartActivity("AccountSettingsPage.UpdateAvatarImage"))
        {
            using (await _clients.Lock.LockAsync())
            {
                activity?.AddEvent(new("Entered_AsyncLock"));

                try
                {
                    if (_clients.AuthorizedUser.Value is { } user)
                    {
                        await user.RefreshAsync();

                        await asset.UpdateAsync(true);
                        await user.Profile.UpdateAsync(avatarId: asset.Id);
                    }
                }
                catch (Exception ex)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    activity?.RecordException(ex);
                    ErrorHandle(ex);
                    _logger.Error(ex, "An unexpected error has occurred.");
                }
            }
        }
    }

    private async Task SignInCore(string? provider = null)
    {
        using (Activity? activity = Telemetry.StartActivity("AccountSettingsPage.SignInCore"))
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
            catch (BeutlApiException<ApiErrorResponse> apiex)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                activity?.RecordException(apiex);
                // Todo: エラー説明
                Error.Value = Message.ApiErrorOccurred;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                activity?.RecordException(ex);
                Error.Value = Message.AnUnexpectedErrorHasOccurred;
            }
            finally
            {
                _cts.Value = null;
            }
        }
    }
}
