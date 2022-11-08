using Beutl.Api;
using Beutl.Framework;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels.SettingsPages;

namespace Beutl.ViewModels;

public sealed class SettingsPageViewModel : IPageContext
{
    public SettingsPageViewModel(BeutlApiApplication clients)
    {
        Account = new AccountSettingsPageViewModel(clients);
        Storage = new StorageSettingsPageViewModel(clients.AuthorizedUser);
    }

    public AccountSettingsPageViewModel Account { get; }

    public StorageSettingsPageViewModel Storage { get; }

    public PageExtension Extension => SettingsPageExtension.Instance;

    public string Header => Strings.Settings;

    public void Dispose()
    {
    }
}
