using System.Reactive.Subjects;

using Beutl.Api;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels.SettingsPages;

namespace Beutl.ViewModels;

public sealed class SettingsPageViewModel : IPageContext
{
    private readonly Subject<object> _navigateRequested = new();
    private readonly Lazy<AccountSettingsPageViewModel> _account;
    private readonly Lazy<StorageSettingsPageViewModel> _storage;
    private readonly Lazy<ViewSettingsPageViewModel> _view;
    private readonly Lazy<EditorSettingsPageViewModel> _editor;
    private readonly Lazy<FontSettingsPageViewModel> _font;
    private readonly Lazy<ExtensionsSettingsPageViewModel> _extensionsPage;
    private readonly Lazy<InfomationPageViewModel> _infomation;

    public SettingsPageViewModel(BeutlApiApplication clients)
    {
        _account = new(() => new AccountSettingsPageViewModel(clients));
        _editor = new(() => new EditorSettingsPageViewModel());
        _view = new(() => new ViewSettingsPageViewModel(_editor));
        _font = new(() => new FontSettingsPageViewModel());
        _extensionsPage = new(() => new ExtensionsSettingsPageViewModel());
        _storage = new(() => new StorageSettingsPageViewModel(clients.AuthorizedUser));
        _infomation = new(() => new InfomationPageViewModel());
    }

    public AccountSettingsPageViewModel Account => _account.Value;

    public ViewSettingsPageViewModel View => _view.Value;
    
    public EditorSettingsPageViewModel Editor => _editor.Value;

    public FontSettingsPageViewModel Font => _font.Value;

    public ExtensionsSettingsPageViewModel ExtensionsPage => _extensionsPage.Value;

    public StorageSettingsPageViewModel Storage => _storage.Value;

    public InfomationPageViewModel Infomation => _infomation.Value;

    public PageExtension Extension => SettingsPageExtension.Instance;

    public string Header => Strings.Settings;

    public IObservable<object> NavigateRequested => _navigateRequested;

    public void GoToSettingsPage()
    {
        _navigateRequested.OnNext(Infomation);
    }

    public void GoToAccountSettingsPage()
    {
        _navigateRequested.OnNext(Account);
    }

    public void Dispose()
    {
    }
}
