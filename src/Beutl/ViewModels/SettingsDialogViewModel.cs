using System.Reactive.Subjects;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Editor.Components.AiWorkspaceTab.Services;
using Beutl.Services;
using Beutl.ViewModels.SettingsPages;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.ViewModels;

public sealed class SettingsDialogViewModel : IDisposable
{
    private readonly INotificationService _notifications;

    private readonly Subject<object> _navigateRequested = new();
    private readonly Lazy<AccountSettingsPageViewModel> _account;
    private readonly Lazy<ViewSettingsPageViewModel> _view;
    private readonly Lazy<EditorSettingsPageViewModel> _editor;
    private readonly Lazy<PreviewSettingsPageViewModel> _preview;
    private readonly Lazy<PerformanceSettingsPageViewModel> _performance;
    private readonly Lazy<VersionControlSettingsPageViewModel> _versionControl;
    private readonly Lazy<PrivacySettingsPageViewModel> _privacy;
    private readonly Lazy<FontSettingsPageViewModel> _font;
    private readonly Lazy<ExtensionsSettingsPageViewModel> _extensionsPage;
    private readonly Lazy<InformationPageViewModel> _information;
    private readonly Lazy<KeyMapSettingsPageViewModel> _keyMap;
    private readonly Lazy<AiAgentSettingsPageViewModel> _aiAgent;
    private readonly Lazy<BrowserSettingsPageViewModel> _browser = new(() => new BrowserSettingsPageViewModel());

    public SettingsDialogViewModel(IServiceProvider services,
        BeutlApiApplication clients,
        ExtensionProvider extensionProvider,
        IAiPlanCoordinator aiPlanCoordinator)
    {
        _notifications = services.GetRequiredService<INotificationService>();
        var configuration = services.GetRequiredService<GlobalConfiguration>();
        ArgumentNullException.ThrowIfNull(aiPlanCoordinator);
        Terminal = new TerminalSettingsPageViewModel(configuration.TerminalConfig);
        _account = new(() => new AccountSettingsPageViewModel(_notifications, clients, aiPlanCoordinator));
        _editor = new(() => new EditorSettingsPageViewModel(configuration));
        _preview = new(() => new PreviewSettingsPageViewModel(configuration.EditorConfig));
        _performance = new(() => new PerformanceSettingsPageViewModel(configuration));
        _versionControl = new(() => new VersionControlSettingsPageViewModel(configuration.VersionControlConfig));
        _privacy = new(() => new PrivacySettingsPageViewModel(configuration.TelemetryConfig));
        _view = new(() => new ViewSettingsPageViewModel(configuration.ViewConfig));
        _font = new(() => new FontSettingsPageViewModel(configuration.FontConfig));
        _extensionsPage = new(() => new ExtensionsSettingsPageViewModel(extensionProvider, services));
        _information = new(() => new InformationPageViewModel(configuration.UpdateConfig));
        _keyMap = new(() => new KeyMapSettingsPageViewModel(clients.GetResource<ContextCommandManager>(), extensionProvider));
        _aiAgent = new(() => new AiAgentSettingsPageViewModel(configuration.AiAgentConfig));
    }

    public TerminalSettingsPageViewModel Terminal { get; }

    public AccountSettingsPageViewModel Account => _account.Value;

    public ViewSettingsPageViewModel View => _view.Value;

    public EditorSettingsPageViewModel Editor => _editor.Value;

    public PreviewSettingsPageViewModel Preview => _preview.Value;

    public PerformanceSettingsPageViewModel Performance => _performance.Value;

    public VersionControlSettingsPageViewModel VersionControl => _versionControl.Value;

    public PrivacySettingsPageViewModel Privacy => _privacy.Value;

    public FontSettingsPageViewModel Font => _font.Value;

    public ExtensionsSettingsPageViewModel ExtensionsPage => _extensionsPage.Value;

    public InformationPageViewModel Information => _information.Value;

    public KeyMapSettingsPageViewModel KeyMap => _keyMap.Value;

    public AiAgentSettingsPageViewModel AiAgent => _aiAgent.Value;

    public BrowserSettingsPageViewModel Browser => _browser.Value;

    public void GoToBrowserSettingsPage() => _navigateRequested.OnNext(Browser);
    public void GoToAiAgentSettingsPage() => _navigateRequested.OnNext(AiAgent);

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
        if (_browser.IsValueCreated) _browser.Value.Dispose();
        if (_account.IsValueCreated)
            _account.Value.Dispose();

        if (_view.IsValueCreated)
            _view.Value.Dispose();

        if (_editor.IsValueCreated)
            _editor.Value.Dispose();
        if (_preview.IsValueCreated)
            _preview.Value.Dispose();
        if (_performance.IsValueCreated)
            _performance.Value.Dispose();
        if (_versionControl.IsValueCreated)
            _versionControl.Value.Dispose();
        if (_privacy.IsValueCreated)
            _privacy.Value.Dispose();

        if (_font.IsValueCreated)
            _font.Value.Dispose();

        if (_extensionsPage.IsValueCreated)
            _extensionsPage.Value.Dispose();

        if (_keyMap.IsValueCreated)
            _keyMap.Value.Dispose();

        if (_aiAgent.IsValueCreated)
            _aiAgent.Value.Dispose();

        if (_information.IsValueCreated)
            _information.Value.Dispose();
    }
}
