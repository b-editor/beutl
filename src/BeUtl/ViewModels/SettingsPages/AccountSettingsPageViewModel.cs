using Avalonia;

using Beutl.Api;
using Beutl.Api.Objects;

using BeUtl.Framework.Service;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace BeUtl.ViewModels.SettingsPages;

public sealed class AccountSettingsPageViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly INotificationService _notification = ServiceLocator.Current.GetRequiredService<INotificationService>();
    private readonly BeutlClients _clients;
    private ReactivePropertySlim<CancellationTokenSource?> cts = new();

    public AccountSettingsPageViewModel(BeutlClients clients)
    {
        _clients = clients;
        SignIn = new(cts.Select(x => x == null));
        SignIn.Subscribe(async () =>
        {
            try
            {
                SigningIn.Value = true;
                cts.Value = new CancellationTokenSource();
                AuthorizedUser? user = await _clients.SignInAsync(cts.Value.Token);
            }
            catch (BeutlApiException<ApiErrorResponse> apiError)
            {
                _notification.Show(new("APIエラーが発生しました", apiError.Result.Message, NotificationType.Error));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _notification.Show(new("予期しないエラーが発生しました", ex.Message, NotificationType.Error));
            }
            finally
            {
                SigningIn.Value = false;
                cts.Value = null;
            }
        });

        Cancel = new(cts.Select(x => x != null));
        Cancel.Subscribe(() => cts.Value!.Cancel());

        SignedIn = clients.AuthorizedUser.Select(x => x != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
    }

    public AsyncReactiveCommand SignIn { get; }

    public ReactiveCommand Cancel { get; }

    public ReactivePropertySlim<bool> SigningIn { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> SignedIn { get; }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
