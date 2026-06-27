using System.Reactive.Subjects;
using Beutl.AgentHost;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.ViewModels.SettingsPages;

namespace Beutl.ViewModels;

public sealed class SettingsDialogViewModel : IDisposable
{
    private readonly Subject<object> _navigateRequested = new();
    private readonly Lazy<AccountSettingsPageViewModel> _account;
    private readonly Lazy<ViewSettingsPageViewModel> _view;
    private readonly Lazy<EditorSettingsPageViewModel> _editor;
    private readonly Lazy<FontSettingsPageViewModel> _font;
    private readonly Lazy<ExtensionsSettingsPageViewModel> _extensionsPage;
    private readonly Lazy<InformationPageViewModel> _information;
    private readonly Lazy<KeyMapSettingsPageViewModel> _keyMap;
    private readonly Lazy<AiAgentSettingsPageViewModel> _aiAgent;

    public SettingsDialogViewModel(
        BeutlApiApplication clients,
        ExtensionProvider extensionProvider,
        AgentHostEndpoint agentHostEndpoint)
    {
        _account = new(() => new AccountSettingsPageViewModel(clients));
        _editor = new(() => new EditorSettingsPageViewModel());
        _view = new(() => new ViewSettingsPageViewModel(_editor));
        _font = new(() => new FontSettingsPageViewModel());
        _extensionsPage = new(() => new ExtensionsSettingsPageViewModel(extensionProvider));
        _information = new(() => new InformationPageViewModel());
        _keyMap = new(() => new KeyMapSettingsPageViewModel(clients.GetResource<ContextCommandManager>(), extensionProvider));
        _aiAgent = new(() => new AiAgentSettingsPageViewModel(agentHostEndpoint));
    }

    public AccountSettingsPageViewModel Account => _account.Value;

    public ViewSettingsPageViewModel View => _view.Value;

    public EditorSettingsPageViewModel Editor => _editor.Value;

    public FontSettingsPageViewModel Font => _font.Value;

    public ExtensionsSettingsPageViewModel ExtensionsPage => _extensionsPage.Value;

    public InformationPageViewModel Information => _information.Value;

    public KeyMapSettingsPageViewModel KeyMap => _keyMap.Value;

    public AiAgentSettingsPageViewModel AiAgent => _aiAgent.Value;

    public IObservable<object> NavigateRequested => _navigateRequested;

    public void GoToSettingsPage()
    {
        _navigateRequested.OnNext(Information);
    }

    public void GoToAccountSettingsPage()
    {
        _navigateRequested.OnNext(Account);
    }

    public void Dispose()
    {
        _navigateRequested.Dispose();
        if (_account.IsValueCreated)
            _account.Value.Dispose();

        if (_view.IsValueCreated)
            _view.Value.Dispose();

        if (_editor.IsValueCreated)
            _editor.Value.Dispose();

        if (_font.IsValueCreated)
            _font.Value.Dispose();

        if (_extensionsPage.IsValueCreated)
            _extensionsPage.Value.Dispose();

        if (_aiAgent.IsValueCreated)
            _aiAgent.Value.Dispose();
    }
}
