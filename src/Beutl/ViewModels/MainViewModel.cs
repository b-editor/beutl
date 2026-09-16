using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Beutl.AgentHost;
using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Editor.Components.VersionControl.ViewModels;
using Beutl.Editor.Services.AI;
using Beutl.Editor.Services.Captions;
using Beutl.Helpers;
using Beutl.Logging;
using Beutl.Services;
using Beutl.Services.AI;
using Beutl.Services.PrimitiveImpls;
using Beutl.Services.StartupTasks;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.ExtensionsPages;
using Beutl.ViewModels.Tools;
using DynamicData;
using DynamicData.Binding;
using Microsoft.Extensions.Logging;
using NuGet.Packaging.Core;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class MainViewModel : BasePageViewModel, IContextCommandHandler
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

    public IReadOnlyReactiveProperty<bool> IsProjectOpened { get; }

    public ReadOnlyObservableCollection<ToolTabExtension> ToolTabExtensions { get; }

    public ReadOnlyObservableCollection<EditorExtension> EditorExtensions { get; }

    public ReadOnlyObservableCollection<ToolWindowExtension> ToolWindowExtensions { get; }

    public ContextCommandManager? ContextCommandManager { get; }

    public CommandPaletteViewModel CommandPalette { get; }

    public SettingsDialogViewModel CreateSettingsDialog()
    {
        return new SettingsDialogViewModel(
            _beutlClients,
            _extensionProvider,
            _agentHostEndpoint,
            _aiPlanCoordinator);
    }

    internal AiImageGenerationDialogViewModel CreateAiImageGenerationToolViewModel(EditViewModel editViewModel)
        => new(
            _beutlClients.GetResource<IAiEntitlementService>(),
            _beutlClients.GetResource<IAiOperationAvailabilityService>(),
            _beutlClients.GetResource<IAiModelCatalogService>(),
            _aiPlanCoordinator,
            _beutlClients.GetResource<IAiImageGenerationService>(),
            _beutlClients.GetResource<IAuthenticatedContentService>(),
            editViewModel,
            _aiRequestRecoveryContext);

    internal AiImageEditDialogViewModel CreateAiImageEditToolViewModel(EditViewModel editViewModel)
        => new(
            _beutlClients.GetResource<IAiEntitlementService>(),
            _beutlClients.GetResource<IAiOperationAvailabilityService>(),
            _beutlClients.GetResource<IAiModelCatalogService>(),
            _aiPlanCoordinator,
            _beutlClients.GetResource<IAiImageEditingService>(),
            _beutlClients.GetResource<IAuthenticatedContentService>(),
            editViewModel,
            _aiRequestRecoveryContext);

    internal AiSubtitleDialogViewModel CreateAiSubtitleToolViewModel(EditViewModel? editViewModel)
    {
        _captionCatalog.RefreshObjectTemplates(
            Beutl.Editor.Services.ObjectTemplateService.Instance.FindByBaseType(
                typeof(Beutl.Graphics.Drawable)));
        return new AiSubtitleDialogViewModel(
            _beutlClients.GetResource<IAiEntitlementService>(),
            _beutlClients.GetResource<IAiOperationAvailabilityService>(),
            _beutlClients.GetResource<IAiModelCatalogService>(),
            _aiPlanCoordinator,
            _beutlClients.GetResource<IAiTranscriptionService>(),
            _beutlClients.GetResource<IAiCaptionTranslationService>(),
            _captionCatalog,
            CaptionDraftStoreProvider.Current,
            CreateCaptionDraftScopes(editViewModel),
            editViewModel);
    }

    private IObservable<CaptionDraftScope?> CreateCaptionDraftScopes(EditViewModel? editViewModel)
        => _beutlClients.AuthenticatedUser.Select(user =>
        {
            Project? project = BeutlApplication.Current.Project;
            return user is null || project is null || editViewModel is null
                ? null
                : new CaptionDraftScope(user.Profile.Id, project.Id, editViewModel.Scene.Id);
        });

    internal AiVideoGenerationDialogViewModel CreateAiVideoGenerationToolViewModel(EditViewModel editViewModel)
        => new(
            _beutlClients.GetResource<IAiEntitlementService>(),
            _beutlClients.GetResource<IAiOperationAvailabilityService>(),
            _beutlClients.GetResource<IAiModelCatalogService>(),
            _aiPlanCoordinator,
            _beutlClients.GetResource<IAiVideoService>(),
            _beutlClients.GetResource<IAuthenticatedContentService>(),
            _beutlClients.GetResource<IAiJobKindRegistry>(),
            _beutlClients.GetResource<IAiJobMonitor>(),
            editViewModel,
            _aiRequestRecoveryContext);

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

    public override void Dispose()
    {
        try
        {
            BeginDisposeOrThrow();
        }
        catch (ProjectCloseAbortedException)
        {
        }
    }

    private void BeginDisposeOrThrow()
    {
        Task<bool>? sharedClose;
        lock (_disposeGate)
        {
            // Disposal is only published after the project close was accepted, and
            // DisposeCoreAsync closes the project again itself, so a repeated request
            // must not pump the UI thread through another synchronous close.
            if (_disposeTask is not null)
                return;

            sharedClose = _closeForShutdownTask;
        }

        if (sharedClose is { IsCompleted: false })
        {
            // A window or shutdown request is already closing the project. Join that
            // attempt instead of opening a second transition that would pump the UI
            // thread behind the project gate and run the close and veto handlers again.
            WaitOnUiThread(sharedClose);
            if (!sharedClose.GetAwaiter().GetResult())
            {
                throw new ProjectCloseAbortedException(
                    "The in-flight project close was refused.");
            }

            return;
        }

        _projectService.CloseProjectOrThrow();
        lock (_disposeGate)
        {
            _disposeTask ??= DisposeCoreAsync();
        }
    }

    private static void WaitOnUiThread(Task task)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            while (!task.IsCompleted)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Thread.Sleep(1);
            }
        }
        else
        {
            task.GetAwaiter().GetResult();
        }
    }

    internal bool TryDisposeForWindowClose()
    {
        try
        {
            BeginDisposeOrThrow();
            return true;
        }
        catch (ProjectCloseAbortedException)
        {
            return false;
        }
    }

    internal Task<bool> TryDisposeForWindowCloseAsync()
    {
        lock (_disposeGate)
        {
            // The window closing path and the desktop shutdown request share one close
            // attempt so that overlapping requests neither save twice nor prompt twice.
            // A vetoed or failed attempt must not answer the next request.
            if (_closeForShutdownTask is { IsCompleted: true } finished
                && !(finished.IsCompletedSuccessfully && finished.Result))
            {
                _closeForShutdownTask = null;
            }

            return _closeForShutdownTask ??= CloseProjectAndBeginDisposeAsync();
        }
    }

    private async Task<bool> CloseProjectAndBeginDisposeAsync()
    {
        try
        {
            await _projectService.CloseProjectAsync();
        }
        catch (ProjectCloseAbortedException)
        {
            return false;
        }

        lock (_disposeGate)
        {
            _disposeTask ??= DisposeCoreAsync();
        }

        return true;
    }

    internal Task WaitForDisposalAsync()
    {
        lock (_disposeGate)
        {
            return _disposeTask ?? Task.CompletedTask;
        }
    }

    internal Task WaitForShutdownRequestAsync()
    {
        lock (_disposeGate)
        {
            return _shutdownRequestTask ?? Task.CompletedTask;
        }
    }

    private bool IsDisposalComplete()
    {
        lock (_disposeGate)
        {
            return _disposeTask is { IsCompleted: true };
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            PackageInstaller packageInstaller = _beutlClients.GetResource<PackageInstaller>();
            _packageInstallerForShutdown = packageInstaller;
            packageInstaller.BeginShutdown();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to begin package installer shutdown.");
        }
        try
        {
            // The host uses project/editor services, so join its complete lifecycle before
            // closing either service.
            await _agentHostEndpoint.StopAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop the agent host during shutdown.");
        }
        try
        {
            _aiJobCompletionNotifier.Dispose();
            CommandPalette.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispose shell notification services during shutdown.");
        }

        try
        {
            TitleBarBranch.Dispose();
            _versionControlCoordinator.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispose version-control shell services during shutdown.");
        }

        await CloseEditorSessionAsync();

        // Recovery source publication markers belong to the editor operations
        // above. Release their cross-process locks only after every tab has
        // canceled and drained its paid-AI work.
        _aiRequestRecoveryContext.Dispose();

        try
        {
            await _aiJobResultHandlers.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to drain AI job result handlers during shutdown.");
        }

        try
        {
            await _captionCatalog.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to drain caption registrations during shutdown.");
        }

        try
        {
            if (ProxyMediaServices.Current is { } proxyMediaServices)
            {
                await proxyMediaServices.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Proxy media services failed to dispose during shutdown.");
        }

        try
        {
            BeutlApplication.Current.Items.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear application services during shutdown.");
        }

        await CompleteShutdownAsync();
    }

    internal async void OpenAiJobCenter()
        => await OpenAiWorkspaceAsync(AiWorkspaceSection.Jobs);

    internal async void OpenAiImageGeneration()
        => await OpenAiWorkspaceAsync(AiWorkspaceSection.ImageGeneration);

    internal async void OpenAiImageEdit()
        => await OpenAiWorkspaceAsync(AiWorkspaceSection.ImageEdit);

    internal async void OpenAiSubtitle(AiCaptionHistoryResult? historyResult = null)
    {
        if (await OpenAiWorkspaceAsync(AiWorkspaceSection.Subtitles) is AiSubtitleDialogViewModel viewModel
            && historyResult is not null)
        {
            viewModel.LoadHistoryResult(historyResult);
        }
    }

    internal async void OpenAiVideoGeneration()
        => await OpenAiWorkspaceAsync(AiWorkspaceSection.VideoGeneration);

    private async Task<object?> OpenAiWorkspaceAsync(AiWorkspaceSection section, EditViewModel? target = null)
    {
        EditViewModel? editorContext = target ?? _editorService.SelectedTabItem.Value?.Context.Value as EditViewModel;
        if (editorContext is null
            || !_editorService.TabItems.Any(item => ReferenceEquals(item.Context.Value, editorContext)))
        {
            return null;
        }

        // A tab already on that page is the one the person means. Otherwise an open
        // AI tab is turned to it, because the menu is a request to see something,
        // not a request for another tab; the tab strip's own button adds those.
        AiWorkspaceViewModel? workspace =
            editorContext.FindToolTab<AiWorkspaceViewModel>(tab => tab.SelectedSection.Value?.Id == section)
            ?? editorContext.FindToolTab<AiWorkspaceViewModel>();

        if (workspace is null)
        {
            workspace = CreateAiWorkspaceViewModel(editorContext);
            if (!await TryOpenNewAiWorkspaceAsync(
                    workspace,
                    () => editorContext.OpenToolTab(workspace)))
            {
                return null;
            }
        }
        else
        {
            if (!editorContext.OpenToolTab(workspace))
            {
                return null;
            }
        }

        return _editorService.TabItems.Any(item => ReferenceEquals(item.Context.Value, editorContext))
            ? workspace.Show(section)
            : null;
    }

    internal async Task<bool> PresentCaptionResultAsync(EditViewModel editor, AiCaptionHistoryResult result)
    {
        if (await OpenAiWorkspaceAsync(AiWorkspaceSection.Subtitles, editor)
            is not AiSubtitleDialogViewModel viewModel)
            return false;
        viewModel.LoadHistoryResult(result);
        return true;
    }

    internal static async Task<bool> TryOpenNewAiWorkspaceAsync(
        AiWorkspaceViewModel workspace,
        Func<bool> tryOpen)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(tryOpen);
        if (tryOpen())
            return true;

        await workspace.DisposeAsync();
        return false;
    }

    internal AiWorkspaceViewModel CreateAiWorkspaceViewModel(EditViewModel editViewModel)
    {
        var workspace = new AiWorkspaceViewModel(
            editViewModel,
            section => CreateAiPage(section, editViewModel));

        // A tab added while another is open is added to see something else, so it
        // starts on the first page no open tab is showing.
        if (FindUnshownSection(editViewModel) is { } section)
        {
            workspace.Show(section);
        }

        return workspace;
    }

    private static AiWorkspaceSection? FindUnshownSection(EditViewModel editViewModel)
    {
        AiWorkspaceSection[] shown = editViewModel.DockHost.Factory.EnumerateTools()
            .Select(tool => tool.ToolContext)
            .OfType<AiWorkspaceViewModel>()
            .Select(tab => tab.SelectedSection.Value?.Id)
            .OfType<AiWorkspaceSection>()
            .ToArray();

        return shown.Length == 0
            ? null
            : Enum.GetValues<AiWorkspaceSection>().Cast<AiWorkspaceSection?>()
                .FirstOrDefault(section => !shown.Contains(section!.Value));
    }

    private IAsyncDisposable CreateAiPage(AiWorkspaceSection section, EditViewModel editViewModel)
        => section switch
        {
            AiWorkspaceSection.ImageGeneration => CreateAiImageGenerationToolViewModel(editViewModel),
            AiWorkspaceSection.ImageEdit => CreateAiImageEditToolViewModel(editViewModel),
            AiWorkspaceSection.VideoGeneration => CreateAiVideoGenerationToolViewModel(editViewModel),
            AiWorkspaceSection.Subtitles => CreateAiSubtitleToolViewModel(editViewModel),
            AiWorkspaceSection.Jobs => CreateAiJobCenterViewModel(editViewModel),
            _ => throw new ArgumentOutOfRangeException(nameof(section)),
        };

    internal AiJobCenterViewModel CreateAiJobCenterViewModel(EditViewModel editViewModel)
        => new(
            editViewModel,
            _beutlClients.GetResource<IAiEntitlementService>(),
            _beutlClients.GetResource<IAuthenticatedContentService>(),
            _beutlClients.GetResource<IAiJobClient>(),
            _beutlClients.GetResource<IAiJobMonitor>(),
            _beutlClients.GetResource<IAiJobKindRegistry>(),
            _aiJobResultHandlers,
            result => PresentCaptionResultAsync(editViewModel, result));

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        Volatile.Write(ref _exitObserved, 1);
        if (sender is IControlledApplicationLifetime lifetime)
        {
            lifetime.Exit -= OnExit;
            if (lifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.ShutdownRequested -= OnShutdownRequested;
            }
        }

        // Exit stops the dispatcher as soon as this handler returns, so anything still
        // pending here is lost. The desktop shutdown request is intercepted below to
        // drain the asynchronous close first; this remains the synchronous fallback for
        // forced Shutdown() callers and non-desktop lifetimes.
        CompleteShutdown();
    }

    internal void RegisterExitHandler(IControlledApplicationLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        lifetime.Exit -= OnExit;
        lifetime.Exit += OnExit;

        if (lifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (_desktopLifetime is { } previous && !ReferenceEquals(previous, desktop))
            {
                previous.Exit -= OnExit;
                previous.ShutdownRequested -= OnShutdownRequested;
            }

            _desktopLifetime = desktop;
            desktop.ShutdownRequested -= OnShutdownRequested;
            desktop.ShutdownRequested += OnShutdownRequested;
        }
    }

    internal void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        // Another handler already refused this request; leave the session untouched.
        if (e.Cancel)
            return;

        // The reissued request, or a window that finished draining on its own: let
        // the lifetime raise Exit and stop the dispatcher.
        if (IsDisposalComplete())
            return;

        IClassicDesktopStyleApplicationLifetime? lifetime =
            sender as IClassicDesktopStyleApplicationLifetime ?? _desktopLifetime;
        if (lifetime is null)
            return;

        // Refuse this request while the close and disposal drain with the dispatcher
        // still running, then ask the lifetime to shut down again. Repeated requests
        // join the in-flight drain instead of starting a second close.
        e.Cancel = true;
        lock (_disposeGate)
        {
            _shutdownRequestTask ??= DrainShutdownRequestAsync(lifetime);
        }
    }

    private async Task DrainShutdownRequestAsync(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        // Publish the in-flight request before a synchronous veto can clear it.
        await Task.Yield();
        bool closeAccepted = false;
        try
        {
            closeAccepted = await TryDisposeForWindowCloseAsync();
            if (closeAccepted)
            {
                await WaitForDisposalAsync();
            }
        }
        catch (Exception ex)
        {
            // A failed close leaves the project open for a retry; once the close was
            // accepted a cached cleanup failure cannot keep the process alive.
            _logger.LogError(ex, "Failed to drain the editor session for a shutdown request.");
            await ex.Handle();
        }

        if (!closeAccepted)
        {
            lock (_disposeGate)
            {
                _shutdownRequestTask = null;
            }

            return;
        }

        // A window that drained in parallel may have completed the shutdown already.
        if (Volatile.Read(ref _exitObserved) != 0)
            return;

        lifetime.TryShutdown();
    }

    internal void CompleteShutdown()
    {
        Dispose();

        Task? disposal;
        lock (_disposeGate)
        {
            disposal = _disposeTask;
        }

        if (disposal is null || disposal.IsCompleted)
            return;

        // Exit stops the dispatcher as soon as the handler returns, so a disposal that
        // is still running (a forced Shutdown() during a window close, for example)
        // would lose its editor teardown and package handoff. Pump it to completion.
        try
        {
            WaitOnUiThread(disposal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Disposal failed while draining for application exit.");
        }
    }

    private async Task CompleteShutdownAsync()
    {
        if (Interlocked.Exchange(ref _shutdownCompleted, 1) != 0)
            return;

        try
        {
            if (_waitForPackageInstallerIdle is not null)
                await _waitForPackageInstallerIdle(TimeSpan.FromSeconds(5));
            else if (_packageInstallerForShutdown is not null)
                await _packageInstallerForShutdown.WaitUntilIdleAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to wait for package operations during shutdown.");
        }

        try
        {
            _shutdownHandoff(_beutlClients);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to hand package changes to the shutdown helper.");
        }
        finally
        {
            await DisposeApiClientsAsync();
        }
    }

    private async Task CloseEditorSessionAsync()
    {
        EditorTabItem[] tabs = _editorService.TabItems.ToArray();
        try
        {
            _editorService.SelectedTabItem.Value = null;
            _editorService.TabItems.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unpublish editor tabs during shutdown.");
        }

        foreach (EditorTabItem tab in tabs)
        {
            try
            {
                await tab.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispose an editor tab during shutdown.");
            }
        }

        try
        {
            await _projectService.CloseProjectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to close the active project during shutdown.");
        }
    }

    private void PerformShutdownHandoff(BeutlApiApplication clients)
    {
        PackageChangesQueue queue = clients.GetResource<PackageChangesQueue>();
        PackageIdentity[] installs = queue.GetInstalls().ToArray();
        PackageIdentity[] uninstalls = queue.GetUninstalls().ToArray();

        if (installs.Length == 0 && uninstalls.Length == 0)
            return;

        var startInfo = new ProcessStartInfo() { UseShellExecute = true, };
        DotNetProcess.Configure(startInfo, Path.Combine(AppContext.BaseDirectory, "Beutl.PackageTools.UI"));

        if (installs.Length > 0)
        {
            startInfo.ArgumentList.Add("--installs");
            foreach (PackageIdentity? item in installs)
            {
                startInfo.ArgumentList.Add(item.HasVersion ? $"{item.Id}/{item.Version}" : item.Id);
            }
        }

        if (uninstalls.Length > 0)
        {
            startInfo.ArgumentList.Add("--uninstalls");
            foreach (PackageIdentity? item in uninstalls)
            {
                startInfo.ArgumentList.Add(item.HasVersion ? $"{item.Id}/{item.Version}" : item.Id);
            }
        }

        startInfo.ArgumentList.AddRange(["--session-id", Telemetry.Instance._sessionId]);

        if (Debugger.IsAttached)
            startInfo.ArgumentList.Add("--launch-debugger");

        Process.Start(startInfo);
    }

    private Task DisposeApiClientsAsync()
    {
        lock (_apiClientDisposeGate)
        {
            return _apiClientDisposeTask ??= DisposeApiClientsCoreAsync();
        }
    }

    private async Task DisposeApiClientsCoreAsync()
    {
        try
        {
            await _beutlClients.DisposeAsync();
        }
        finally
        {
            _authHttpClient.Dispose();
        }
    }

    public Task ExecuteAsync(ContextCommandExecution execution)
    {
        if (execution.KeyEventArgs != null)
            execution.KeyEventArgs.Handled = true;

        if (execution.CommandName == "ShowCommandPalette")
        {
            CommandPalette.Toggle();
            return Task.CompletedTask;
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
        if (execution.CommandName == "ShowCommandPalette")
            return true;

        // 未知のコマンドは false を返し、ContextCommandManager のフォールバックバインディングや
        // 他のハンドラーへキーイベントを委ねられるようにする。
        return MenuBar.FindContextCommand(execution.CommandName)?.CanExecute(null) ?? false;
    }
}
