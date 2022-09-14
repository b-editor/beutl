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
        SignIn = new();
        SignIn.Subscribe(async () =>
        {
            cts.Value = new CancellationTokenSource();
            AuthorizedUser? user = await _clients.SignInAsync(cts.Value.Token);
        });

        Cancel = new(cts.Select(x => x != null));
        Cancel.Subscribe(() =>
        {
            cts.Value!.Cancel();
        });
    }

    public AsyncReactiveCommand SignIn { get; }

    public ReactiveCommand Cancel { get; }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
