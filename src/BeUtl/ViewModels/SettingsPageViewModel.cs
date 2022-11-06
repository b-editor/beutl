using Beutl.Api;

using Beutl.ViewModels.SettingsPages;

namespace Beutl.ViewModels;

public sealed class SettingsPageViewModel
{
    public SettingsPageViewModel(BeutlApiApplication clients)
    {
        Account = new AccountSettingsPageViewModel(clients);
        Storage = new StorageSettingsPageViewModel(clients.AuthorizedUser);
    }

    public AccountSettingsPageViewModel Account { get; }

    public StorageSettingsPageViewModel Storage { get; }
}
