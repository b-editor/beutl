using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Beutl.AgentHost;
using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;
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
    private readonly ExtensionProvider _extensionProvider;
    private readonly CaptionCatalog _captionCatalog;
    private readonly AiJobResultHandlerRegistry _aiJobResultHandlers;
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
    private Task? _disposeTask;
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
        _aiJobResultHandlers = new AiJobResultHandlerRegistry(
            BuiltInAiJobResultHandlers.Create(),
            _extensionProvider,
            failure => _logger.LogWarning(
                failure.Exception,
                "Ignoring invalid AI job result contribution from {ExtensionType}.",
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
        ContextCommandManager = _beutlClients.GetResource<ContextCommandManager>();
        _aiJobCompletionNotifier = new AiJobCompletionNotifier(
            _beutlClients.GetResource<IAiJobMonitor>().Snapshot,
            _beutlClients.GetResource<IAiJobKindRegistry>(),
            _aiJobResultHandlers,
            OpenAiJobCenter);

        MenuBar = new MenuBarViewModel(_projectService, _editorService);

        IsProjectOpened = _projectService.IsOpened;
        NameOfOpenProject = _projectService.CurrentProject.Select(v =>
                v is { Uri.LocalPath: { } path } ? Path.GetFileName(path) : null)
            .ToReadOnlyReactivePropertySlim();
        WindowTitle = NameOfOpenProject.Select(v => string.IsNullOrWhiteSpace(v) ? "Beutl" : $"Beutl - {v}")
            .ToReadOnlyReactivePropertySlim("Beutl");
        TitleBreadcrumbBar = new TitleBreadcrumbBarViewModel(this, _editorService);

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

    public EditorHostViewModel EditorHost { get; }

    // Exposed so views bound to this composition root (MainView, MacWindow) can read the
    // injected singletons via their DataContext.
    internal ProjectService ProjectService => _projectService;

    internal EditorService EditorService => _editorService;

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
        lock (_disposeGate)
        {
            _disposeTask ??= DisposeCoreAsync();
        }
    }

    internal Task WaitForDisposalAsync()
    {
        lock (_disposeGate)
        {
            return _disposeTask ?? Task.CompletedTask;
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

        await CloseEditorSessionAsync(
            _projectService.CloseProjectAsync,
            EditorHost.DisposeAsync,
            (exception, message) => _logger.LogError(exception, message));

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

    private async Task<object?> OpenAiWorkspaceAsync(AiWorkspaceSection section)
    {
        if (_editorService.SelectedTabItem.Value?.Context.Value is not EditViewModel editorContext)
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
            if (!await editorContext.OpenToolTabAsync(workspace))
            {
                return null;
            }
        }
        else
        {
            if (!await editorContext.OpenToolTabAsync(workspace))
            {
                return null;
            }
        }

        return workspace.Show(section);
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
            OpenAiSubtitle);

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (sender is IControlledApplicationLifetime lifetime)
        {
            lifetime.Exit -= OnExit;
        }

        CompleteShutdown();
    }

    internal void RegisterExitHandler(IControlledApplicationLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        lifetime.Exit -= OnExit;
        lifetime.Exit += OnExit;
    }

    internal void CompleteShutdown()
        => Dispose();

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

    internal static async Task CloseEditorSessionAsync(
        Func<Task> closeProject,
        Func<ValueTask> disposeEditorHost,
        Action<Exception, string> logError)
    {
        ArgumentNullException.ThrowIfNull(closeProject);
        ArgumentNullException.ThrowIfNull(disposeEditorHost);
        ArgumentNullException.ThrowIfNull(logError);

        try
        {
            await closeProject();
        }
        catch (Exception ex)
        {
            logError(ex, "Failed to close the active project during shutdown.");
        }

        try
        {
            await disposeEditorHost();
        }
        catch (Exception ex)
        {
            logError(ex, "Failed to dispose the editor host during shutdown.");
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

    public void Execute(ContextCommandExecution execution)
    {
        if (execution.KeyEventArgs != null)
            execution.KeyEventArgs.Handled = true;

        if (execution.CommandName == "ShowCommandPalette")
        {
            CommandPalette.Toggle();
            return;
        }

        if (MenuBar.FindContextCommand(execution.CommandName) is { } command)
        {
            if (command.CanExecute(null))
                command.Execute(null);
            return;
        }

        if (execution.KeyEventArgs != null)
            execution.KeyEventArgs.Handled = false;
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
