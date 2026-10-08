using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Beutl.AgentHost;
using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Editor;
using Beutl.Editor.Components.VersionControl.ViewModels;
using Beutl.Editor.Services.AI;
using Beutl.Editor.Services.Captions;
using Beutl.Editor.VersionControl;
using Beutl.Logging;
using Beutl.Services;
using Beutl.Services.AI;
using Beutl.Services.PrimitiveImpls;
using Beutl.Services.StartupTasks;
using Beutl.ViewModels.ExtensionsPages;
using DynamicData;
using DynamicData.Binding;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed partial class MainViewModel : BasePageViewModel, IContextCommandHandler
{
    internal readonly BeutlApiApplication _beutlClients;
    private readonly HttpClient _authHttpClient;
    private readonly ProjectService _projectService;
    private readonly EditorService _editorService;
    private readonly VersionControlCoordinator _versionControlCoordinator;
    private readonly ExtensionProvider _extensionProvider;
    private readonly CaptionCatalog _captionCatalog;
    private readonly AiJobResultRegistry _aiJobResultHandlers;
    private readonly AgentHostEndpoint _agentHostEndpoint;
    private readonly IAiPlanCoordinator _aiPlanCoordinator;
    private readonly AiRequestRecoveryContext _aiRequestRecoveryContext;
    private readonly ILogger _logger = Log.CreateLogger<MainViewModel>();
    private readonly AiJobCompletionNotifier _aiJobCompletionNotifier;
    private readonly Action<BeutlApiApplication> _shutdownHandoff;
    private readonly Func<TimeSpan, Task>? _waitForPackageInstallerIdle;
    private PackageInstaller? _packageInstallerForShutdown;
    private readonly object _disposeGate = new();
    private readonly object _apiClientDisposeGate = new();
    private int _shutdownCompleted;
    private int _exitObserved;
    private Task? _disposeTask;
    private Task<bool>? _closeForShutdownTask;
    private Task? _shutdownRequestTask;
    private IClassicDesktopStyleApplicationLifetime? _desktopLifetime;
    private Task? _apiClientDisposeTask;

    public MainViewModel()
        : this(null)
    {
    }

    internal MainViewModel(
        Action<BeutlApiApplication>? shutdownHandoff,
        Func<TimeSpan, Task>? waitForPackageInstallerIdle = null)
    {
        _shutdownHandoff = shutdownHandoff ?? PerformShutdownHandoff;
        _authHttpClient = new HttpClient();
        // Composition root: own the editor-session services here and thread the instances
        // down to child view models and services.
        _extensionProvider = new ExtensionProvider();
        _projectService = new ProjectService();
        _editorService = new EditorService(_extensionProvider);
        _versionControlCoordinator = new VersionControlCoordinator(_projectService, _editorService);
        _captionCatalog = CaptionCatalog.ComposeWithDefaultElementFactory(
            Beutl.Language.Strings.AiSubtitle_DefaultTemplate,
            Beutl.Editor.Services.ObjectTemplateService.Instance.FindByBaseType(
                typeof(Beutl.Graphics.Drawable)),
            _extensionProvider,
            CaptionPresentationDefaults.ElementFactory,
            failure => _logger.LogWarning(
                failure.Exception,
                "Ignoring invalid caption {ContributionKind} contribution from {ExtensionName}.",
                failure.Kind,
                failure.ExtensionName));
        _aiJobResultHandlers = BuiltInAiJobResultCapabilities.CreateRegistry(
            _extensionProvider,
            failure => _logger.LogWarning(
                failure.Exception,
                "Ignoring invalid AI job result {Capability} contribution from {ExtensionType}.",
                failure.Capability,
                failure.ExtensionType));
        _agentHostEndpoint = new AgentHostEndpoint(_projectService, _editorService);
        _beutlClients = new BeutlApiApplication(_authHttpClient, _extensionProvider);
        _waitForPackageInstallerIdle = waitForPackageInstallerIdle;
        _aiRequestRecoveryContext = new AiRequestRecoveryContext(
            new FileAiRequestRecoveryStore(Path.Combine(
                BeutlEnvironment.GetHomeDirectoryPath(),
                "ai")),
            () => _beutlClients.AuthenticatedUser.Value is { } user
                ? new AiAuthenticatedRequestIdentity(user.Profile.Id, user)
                : null,
            _beutlClients.AuthenticatedUser.Select<AuthenticatedUser?, AiAuthenticatedRequestIdentity?>
                (user => user is { } authenticated
                    ? new AiAuthenticatedRequestIdentity(authenticated.Profile.Id, authenticated)
                    : null));
        _aiPlanCoordinator = new AiPlanCoordinator(
            _beutlClients.GetResource<IAiEntitlementService>());
        _editorService.BrowserSettingsHost = new BrowserSettingsHost(CreateSettingsDialog);
        _editorService.StorageProviders = new Beutl.Editor.Components.FileBrowserTab.FileBrowserStorageProviderRegistry(
            new BeutlStorageProvider(_beutlClients, CreateSettingsDialog));
        ContextCommandManager = _beutlClients.GetResource<ContextCommandManager>();
        _aiJobCompletionNotifier = new AiJobCompletionNotifier(
            _beutlClients.GetResource<IAiJobMonitor>().Snapshot,
            _beutlClients.GetResource<IAiJobKindRegistry>(),
            _aiJobResultHandlers,
            OpenAiJobCenter);

        MenuBar = new MenuBarViewModel(_projectService, _editorService, _versionControlCoordinator);
        ProjectDiskDeletion = new ProjectDiskDeletion(_projectService, _editorService);

        IsProjectOpened = _projectService.IsOpened;
        NameOfOpenProject = _projectService.CurrentProject.Select(v =>
                v is { Uri.LocalPath: { } path } ? Path.GetFileName(path) : null)
            .ToReadOnlyReactivePropertySlim();
        WindowTitle = NameOfOpenProject.Select(v => string.IsNullOrWhiteSpace(v) ? "Beutl" : $"Beutl - {v}")
            .ToReadOnlyReactivePropertySlim("Beutl");
        TitleBreadcrumbBar = new TitleBreadcrumbBarViewModel(this, _editorService);
        TitleBarBranch = new TitleBarBranchViewModel(
            _editorService.ProjectVersionControlService,
            _versionControlCoordinator.IsGitAvailable,
            _versionControlCoordinator);

        EditorHost = new EditorHostViewModel(_projectService, _editorService);

        var paletteService = new CommandPaletteService(
            ContextCommandManager,
            new CommandPaletteHandlerProvider(() => this, _editorService),
            () => MenuBar,
            _editorService,
            _extensionProvider);
        CommandPalette = new CommandPaletteViewModel(paletteService, _editorService);
        TabSwitcher = new TabSwitcherViewModel(_editorService);

        ICoreReadOnlyList<Extension> allExtension = _extensionProvider.AllExtensions;

        var comparer = SortExpressionComparer<Extension>.Ascending(i => i.Name);
        IObservable<IChangeSet<Extension>> changeSet = allExtension
            .ToObservableChangeSet<ICoreReadOnlyList<Extension>, Extension>()
            .Sort(comparer);

        changeSet.Filter(i => i is ToolTabExtension)
            .Cast(item => (ToolTabExtension)item)
            .Bind(out ReadOnlyObservableCollection<ToolTabExtension>? list1)
            .Subscribe();

        changeSet.Filter(i => i is EditorExtension)
            .Cast(item => (EditorExtension)item)
            .Bind(out ReadOnlyObservableCollection<EditorExtension>? list2)
            .Subscribe();

        changeSet.Filter(i => i is ToolWindowExtension)
            .Cast(item => (ToolWindowExtension)item)
            .Bind(out ReadOnlyObservableCollection<ToolWindowExtension>? list4)
            .Subscribe();

        ToolTabExtensions = list1;
        EditorExtensions = list2;
        ToolWindowExtensions = list4;
    }

    public bool IsDebuggerAttached { get; } = Debugger.IsAttached;

    public ReactivePropertySlim<bool> IsRunningStartupTasks { get; } = new();

    public ReadOnlyReactivePropertySlim<string?> NameOfOpenProject { get; }

    public ReadOnlyReactivePropertySlim<string> WindowTitle { get; }

    public MenuBarViewModel MenuBar { get; }

    public TitleBreadcrumbBarViewModel TitleBreadcrumbBar { get; }

    internal TitleBarBranchViewModel TitleBarBranch { get; }

    public EditorHostViewModel EditorHost { get; }

    // Exposed so views bound to this composition root (MainView, MacWindow) can read the
    // injected singletons via their DataContext.
    internal ProjectService ProjectService => _projectService;

    internal EditorService EditorService => _editorService;

    internal VersionControlCoordinator VersionControlCoordinator => _versionControlCoordinator;

    internal ExtensionProvider ExtensionProvider => _extensionProvider;

    internal AgentHostEndpoint AgentHostEndpoint => _agentHostEndpoint;

    internal ProjectDiskDeletion ProjectDiskDeletion { get; }

    internal async Task<ExportResult> ExportProjectAsync(
        Project project,
        string outputPath,
        IProgress<(string Message, double Progress)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return await Dispatcher.UIThread.InvokeAsync(() => ExportProjectAsync(project, outputPath, progress, cancellationToken));

        cancellationToken.ThrowIfCancellationRequested();
        ExportResult result = new(false, []);
        await _projectService.RunExclusiveOfTransitionsAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            // The native picker may have stayed open while another operation changed projects.
            if (!ReferenceEquals(_projectService.CurrentProject.Value, project))
                return;

            using IDisposable output = _editorService.BeginObservedOutputOperation();
            using IDisposable suspension = _editorService.SuspendEditors();
            using IProjectFileWriteLease fileWrite = await _editorService.BeginProjectFileWriteAsync(cancellationToken);
            foreach (EditViewModel editor in _editorService.TabItems.Select(tab => tab.Context.Value).OfType<EditViewModel>().ToArray())
            {
                editor.HistoryManager.FlushPendingMutations();
                editor.HistoryManager.Commit();
            }

            if (!await _editorService.SaveProjectFilesAsync(project, cancellationToken))
                throw new IOException(MessageStrings.FileSaveException);

            // Keep the saved graph and its files stable while the package service copies them.
            result = await ProjectPackageService.Current.ExportAsync(project, outputPath, progress, cancellationToken);
        });
        return result;
    }

    public IReadOnlyReactiveProperty<bool> IsProjectOpened { get; }

    public ReadOnlyObservableCollection<ToolTabExtension> ToolTabExtensions { get; }

    public ReadOnlyObservableCollection<EditorExtension> EditorExtensions { get; }

    public ReadOnlyObservableCollection<ToolWindowExtension> ToolWindowExtensions { get; }

    public ContextCommandManager? ContextCommandManager { get; }

    public CommandPaletteViewModel CommandPalette { get; }
    public TabSwitcherViewModel TabSwitcher { get; }

    public SettingsDialogViewModel CreateSettingsDialog()
    {
        return new SettingsDialogViewModel(
            _beutlClients,
            _extensionProvider,
            _agentHostEndpoint,
            _aiPlanCoordinator);
    }

    public Startup RunStartupTask()
    {
        IsRunningStartupTasks.Value = true;
        var startup = new Startup(_beutlClients, _projectService, _editorService);
        startup.WaitAll().ContinueWith(_ => IsRunningStartupTasks.Value = false);

        return startup;
    }

    public void RegisterServices()
    {
        if (Application.Current is { ApplicationLifetime: IControlledApplicationLifetime lifetime })
        {
            RegisterExitHandler(lifetime);
        }

        _agentHostEndpoint.StartInBackground();
    }

    public Task ExecuteAsync(ContextCommandExecution execution)
    {
        if (TabSwitcherViewModel.IsNavigationCommand(execution.CommandName))
        {
            bool opened = !TabSwitcherViewModel.IsTextInputGesture(execution.KeyEventArgs)
                          && (execution.KeyEventArgs is null || !CommandPalette.IsOpen.Value)
                          && TabSwitcher.ExecuteCommand(execution);
            if (execution.KeyEventArgs is { } args) args.Handled = opened;
            return Task.CompletedTask;
        }

        if (execution.KeyEventArgs != null)
            execution.KeyEventArgs.Handled = true;

        if (execution.CommandName == "ShowCommandPalette")
        {
            TabSwitcher.Close();
            CommandPalette.Toggle();
            return Task.CompletedTask;
        }

        if (execution.Interaction is { } interaction
            && ExecutePaletteCommand(execution.CommandName, interaction) is { } paletteOperation)
        {
            return paletteOperation;
        }

        if (MenuBar.FindContextCommand(execution.CommandName) is { } command)
        {
            return MenuBarViewModel.ExecuteCommandAsync(command);
        }

        if (execution.KeyEventArgs != null)
            execution.KeyEventArgs.Handled = false;

        return Task.CompletedTask;
    }

    public bool CanExecute(ContextCommandExecution execution)
    {
        if (TabSwitcherViewModel.IsNavigationCommand(execution.CommandName))
            return TabSwitcher.CanOpen
                   && !TabSwitcherViewModel.IsTextInputGesture(execution.KeyEventArgs)
                   && (execution.KeyEventArgs is null || !CommandPalette.IsOpen.Value);

        if (execution.CommandName == "ShowCommandPalette")
            return true;

        if (CanExecutePaletteOnlyCommand(execution.CommandName) is { } canExecute)
            return canExecute;

        // 未知のコマンドは false を返し、ContextCommandManager のフォールバックバインディングや
        // 他のハンドラーへキーイベントを委ねられるようにする。
        return MenuBar.FindContextCommand(execution.CommandName)?.CanExecute(null) ?? false;
    }
}
