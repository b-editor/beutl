using Beutl.Api;

using BeUtl.ViewModels.SettingsPages;

namespace BeUtl.ViewModels;

public sealed class SettingsPageViewModel
{
    public SettingsPageViewModel(BeutlClients clients)
    {
        Account = new AccountSettingsPageViewModel(clients);
        Storage = new StorageSettingsPageViewModel(clients.AuthorizedUser);
    }

    public AccountSettingsPageViewModel Account { get; }

    public StorageSettingsPageViewModel Storage { get; }
}
