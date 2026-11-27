using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Beutl.AgentHost;
using Beutl.Agents;
using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Editor.Components.AiWorkspaceTab.Services;
using Beutl.Editor.Components.VersionControl.ViewModels;
using Beutl.Editor.Services.AI;
using Beutl.Editor.Services.Captions;
using Beutl.Editor.VersionControl;
using Beutl.Logging;
using Beutl.Media.Decoding;
using Beutl.Services;
using Beutl.Services.AI;
using Beutl.Services.PrimitiveImpls;
using Beutl.Services.StartupTasks;
using Beutl.ViewModels.ExtensionsPages;
using DynamicData;
using DynamicData.Binding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed partial class MainViewModel : BasePageViewModel
{
    internal readonly BeutlApiApplication _beutlClients;
    private readonly ObjectTemplateService _objectTemplateService;
    internal IServiceProvider AppServices { get; }
    internal GlobalConfiguration Configuration { get; }
    internal string SessionId { get; }
    private readonly HttpClient _authHttpClient;
    private readonly ProjectService _projectService;
    private readonly EditorService _editorService;
    private readonly VersionControlCoordinator _versionControlCoordinator;
    private readonly ExtensionProvider _extensionProvider;
    private readonly CaptionCatalog _captionCatalog;
    private readonly AiJobResultRegistry _aiJobResultHandlers;
    private readonly AgentHostEndpoint _agentHostEndpoint;
    private readonly AgentRuntimeManager _agentRuntimeManager;
    private readonly IAiPlanCoordinator _aiPlanCoordinator;
    private readonly AiRequestRecoveryContext _aiRequestRecoveryContext;
    private readonly ILogger _logger = Log.CreateLogger<MainViewModel>();
    private readonly AiJobCompletionNotifier _aiJobCompletionNotifier;
    private readonly Action<BeutlApiApplication> _shutdownHandoff;
    private readonly Func<TimeSpan, Task>? _waitForPackageInstallerIdle;
    private PackageInstaller? _packageInstallerForShutdown;
    private readonly object _disposeGate = new();
    private int _exitObserved;
    private Task? _disposeTask;
    private Task<bool>? _closeForShutdownTask;
    private Task? _shutdownRequestTask;
    private IClassicDesktopStyleApplicationLifetime? _desktopLifetime;
    private readonly ProjectColorPaletteHost _projectColorPalettes;
    private readonly IDisposable _projectColorPalettesAttachment;

    public MainViewModel(IServiceProvider appServices, string sessionId)
        : this(appServices, sessionId, null)
    {
    }

    internal MainViewModel(
        IServiceProvider appServices,
        string sessionId,
        Action<BeutlApiApplication>? shutdownHandoff,
        Func<TimeSpan, Task>? waitForPackageInstallerIdle = null)
    {
        AppServices = appServices;
        Configuration = appServices.GetRequiredService<GlobalConfiguration>();
        SessionId = sessionId;
        _objectTemplateService = appServices.GetRequiredService<ObjectTemplateService>();
        _shutdownHandoff = shutdownHandoff ?? PerformShutdownHandoff;
        _authHttpClient = new HttpClient();
        // Composition root: own the editor-session services here and thread the instances
        // down to child view models and services.
        _extensionProvider = new ExtensionProvider(Configuration.ExtensionConfig, appServices.GetRequiredService<INotificationService>());
        _editorService = new EditorService(_extensionProvider, AppServices);
        _projectService = new ProjectService(Configuration.ViewConfig, appServices.GetRequiredService<Beutl.Media.FontManager>(),
            appServices.GetRequiredService<INotificationService>(), _editorService);
        _editorService.ProjectFonts = _projectService.GetFonts;
        _editorService.ProjectAssets = new ProjectAssetCoordinator(_projectService, _editorService,
            appServices.GetRequiredService<DecoderRegistry>());
        _versionControlCoordinator = new VersionControlCoordinator(_projectService, _editorService);
        // After version control, whose preparation can refuse the close.
        _ = new ProjectThumbnailService(_projectService, _editorService, _versionControlCoordinator);
        _projectColorPalettes = new ProjectColorPaletteHost(_projectService, _editorService);
        _projectColorPalettesAttachment = appServices.GetRequiredService<Beutl.Controls.PropertyEditors.ColorPaletteService>()
            .AttachProjectHost(_projectColorPalettes);
        _captionCatalog = CaptionCatalog.ComposeWithDefaultElementFactory(
            Beutl.Language.Strings.AiSubtitle_DefaultTemplate,
            _objectTemplateService.FindByBaseType(
                typeof(Beutl.Graphics.Drawable)),
            CaptionPresentationDefaults.ElementFactory);
        _aiJobResultHandlers = BuiltInAiJobResultCapabilities.CreateRegistry();
        _beutlClients = new BeutlApiApplication(_authHttpClient, _extensionProvider, Configuration.ViewConfig, Configuration.UpdateConfig, AppServices);
        _editorService.ApiApplication = _beutlClients;
        _waitForPackageInstallerIdle = waitForPackageInstallerIdle;
        _aiRequestRecoveryContext = new AiRequestRecoveryContext(
            new FileAiRequestKeyMap(Path.Combine(
                BeutlEnvironment.GetHomeDirectoryPath(),
                "ai")),
            () => _beutlClients.AuthenticatedUser.Value is { } user
                ? new AiAuthenticatedRequestIdentity(user.Profile.Id, user)
                : null,
            _beutlClients.AuthenticatedUser.Select<AuthenticatedUser?, AiAuthenticatedRequestIdentity?>
                (user => user is { } authenticated
                    ? new AiAuthenticatedRequestIdentity(authenticated.Profile.Id, authenticated)
                    : null));
        // Agents generate with the signed-in account and the AI nodes' executor.
        _agentHostEndpoint = new AgentHostEndpoint(_projectService, _editorService, new AgentHostAiBackend(
            _aiRequestRecoveryContext,
            _beutlClients.GetResource<IAiEntitlementService>(),
            _beutlClients.GetResource<IAiOperationAvailabilityService>(),
            _beutlClients.GetResource<IAiTranscriptionService>(),
            CreateGenerativeModelCatalog,
            CreateGenerativeNodeExecutor,
            _beutlClients.GetResource<IAiModelCatalogService>(), appServices.GetRequiredService<DecoderRegistry>(),
            _beutlClients.GetResource<IAiJobClient>()));
        _aiPlanCoordinator = new AiPlanCoordinator(
            _beutlClients.GetResource<IAiEntitlementService>(), Configuration.ViewConfig);
        _editorService.BrowserSettingsHost = new BrowserSettingsHost(CreateSettingsDialog);
        _editorService.StorageProviders = new Beutl.Editor.Components.FileBrowserTab.FileBrowserStorageProviderRegistry(
            new BeutlStorageProvider(AppServices.GetRequiredService<INotificationService>(), _beutlClients, CreateSettingsDialog));
        _editorService.AiWorkspaceHost = this;
        string beutlHome = Path.GetFullPath(BeutlEnvironment.GetHomeDirectoryPath());
        var agentLogger = Log.CreateLogger<AgentRuntimeManager>();
        _agentRuntimeManager = new AgentRuntimeManager(new WorkerAgentRuntimeBridge(logger: agentLogger),
            new AgentThreadStore(beutlHome), logger: agentLogger);
        _editorService.AgentWorkspaceHost = new BeutlAgentWorkspaceHost(
            _agentRuntimeManager, _agentHostEndpoint, Configuration.AiAgentConfig, beutlHome);
        ContextCommandManager = _beutlClients.GetResource<ContextCommandManager>();
        _aiJobCompletionNotifier = new AiJobCompletionNotifier(
            _beutlClients.GetResource<IAiJobMonitor>().Snapshot,
            _beutlClients.GetResource<IAiJobKindRegistry>(),
            _aiJobResultHandlers,
            OpenAiJobCenter,
            appServices.GetRequiredService<INotificationService>());

        RepositoryClone = new RepositoryCloneWorkflow(_beutlClients, _projectService, _versionControlCoordinator, Configuration);
        MenuBar = new MenuBarViewModel(_projectService, _editorService, _versionControlCoordinator);
        ProjectDiskDeletion = new ProjectDiskDeletion(_projectService, _editorService);

        IsProjectOpened = _projectService.IsOpened;
        NameOfOpenProject = _projectService.CurrentProject.Select(v =>
                v is { Uri.LocalPath: { } path } ? Path.GetFileName(path) : null)
            .ToReadOnlyReactivePropertySlim();
        WindowTitle = NameOfOpenProject.Select(v => string.IsNullOrWhiteSpace(v) ? "Beutl" : $"Beutl - {v}")
            .ToReadOnlyReactivePropertySlim("Beutl");
        TitleBreadcrumbBar = new TitleBreadcrumbBarViewModel(this, _editorService);
        TitleBarBranch = new TitleBarBranchViewModel(AppServices.GetRequiredService<INotificationService>(),
            _editorService.ProjectVersionControlService,
            _versionControlCoordinator.IsGitAvailable,
            _versionControlCoordinator,
            _beutlClients);
        Status = new TitleBarStatusViewModel(
            IsRunningStartupTasks,
            _editorService,
            _agentHostEndpoint,
            _beutlClients.AuthenticatedUser,
            _beutlClients.GetResource<IAiJobMonitor>().Snapshot,
            _beutlClients.GetResource<IAiJobKindRegistry>(),
            OpenAiJobCenter,
            AppServices.GetRequiredService<AppUpdateController>().Status,
            AppServices.GetRequiredService<AppUpdateController>().ShowDialog);

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

        MenuBar.OpenToolTab.Subscribe(OpenToolTab);
        MenuBar.SwitchEditor.Subscribe(SwitchEditor);
        MenuItems = CreateMenuItems();
    }

    public bool IsDebuggerAttached { get; } = Debugger.IsAttached;

    public ReactivePropertySlim<bool> IsRunningStartupTasks { get; } = new();

    public ReadOnlyReactivePropertySlim<string?> NameOfOpenProject { get; }

    public ReadOnlyReactivePropertySlim<string> WindowTitle { get; }

    public MenuBarViewModel MenuBar { get; }

    public TitleBreadcrumbBarViewModel TitleBreadcrumbBar { get; }

    internal TitleBarBranchViewModel TitleBarBranch { get; }

    public TitleBarStatusViewModel Status { get; }

    public EditorHostViewModel EditorHost { get; }

    // Exposed so views bound to this composition root (MainView, MacWindow) can read the
    // injected singletons via their DataContext.
    internal ProjectService ProjectService => _projectService;

    internal EditorService EditorService => _editorService;

    internal VersionControlCoordinator VersionControlCoordinator => _versionControlCoordinator;

    internal RepositoryCloneWorkflow RepositoryClone { get; }

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
            result = await ProjectPackageService.Current.ExportAsync(project, _projectService.GetFonts(project), AppServices.GetRequiredService<DecoderRegistry>(), outputPath, progress, cancellationToken);
        });
        return result;
    }

    public IReadOnlyReactiveProperty<bool> IsProjectOpened { get; }

    public ReadOnlyObservableCollection<ToolTabExtension> ToolTabExtensions { get; }

    public ReadOnlyObservableCollection<EditorExtension> EditorExtensions { get; }

    public ReadOnlyObservableCollection<ToolWindowExtension> ToolWindowExtensions { get; }

    public ContextCommandManager ContextCommandManager { get; }

    public CommandPaletteViewModel CommandPalette { get; }
    public TabSwitcherViewModel TabSwitcher { get; }

    public SettingsDialogViewModel CreateSettingsDialog()
    {
        return new SettingsDialogViewModel(AppServices,
            _beutlClients,
            _extensionProvider,
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

    internal static bool TryGetCurrent([NotNullWhen(true)] out MainViewModel? main)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime
            && lifetime.MainWindow?.DataContext is MainViewModel found)
        {
            main = found;
            return true;
        }

        main = null;
        return false;
    }
}
