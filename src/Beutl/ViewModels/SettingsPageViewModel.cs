using System.Reactive.Subjects;

using Beutl.Api;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels.SettingsPages;

namespace Beutl.ViewModels;

public sealed class SettingsPageViewModel : IPageContext
{
    private readonly Subject<object> _navigateRequested = new();

    public SettingsPageViewModel(BeutlApiApplication clients)
    {
        Account = new AccountSettingsPageViewModel(clients);
        View = new ViewSettingsPageViewModel();
        Font = new FontSettingsPageViewModel();
        ExtensionsPage = new ExtensionsSettingsPageViewModel();
        Storage = new StorageSettingsPageViewModel(clients.AuthorizedUser);
        Infomation = new InfomationPageViewModel();
    }

    public AccountSettingsPageViewModel Account { get; }

    public ViewSettingsPageViewModel View { get; }

    public FontSettingsPageViewModel Font { get; }

    public ExtensionsSettingsPageViewModel ExtensionsPage { get; }

    public StorageSettingsPageViewModel Storage { get; }

    public InfomationPageViewModel Infomation { get; }

    public PageExtension Extension => SettingsPageExtension.Instance;

    public string Header => Strings.Settings;

    public IObservable<object> NavigateRequested => _navigateRequested;

    public void GoToSettingsPage()
    {
        _navigateRequested.OnNext(Infomation);
    }

    public void Dispose()
    {
    }
}
