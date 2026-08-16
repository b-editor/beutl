using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Beutl.AgentHost;
using Beutl.Api;
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
    private readonly ILogger _logger = Log.CreateLogger<MainViewModel>();
    private readonly AiJobCompletionNotifier _aiJobCompletionNotifier;
    private readonly Action<BeutlApiApplication> _shutdownHandoff;
    private readonly object _disposeGate = new();
    private readonly object _apiClientDisposeGate = new();
    private int _shutdownCompleted;
    private Task? _disposeTask;
    private Task? _apiClientDisposeTask;

    public MainViewModel()
        : this(null)
    {
    }

    internal MainViewModel(Action<BeutlApiApplication>? shutdownHandoff)
    {
        _shutdownHandoff = shutdownHandoff ?? PerformShutdownHandoff;
        _authHttpClient = new HttpClient();
        // Composition root: own the editor-session services here and thread the instances
        // down to child view models and services.
        _extensionProvider = new ExtensionProvider();
        _projectService = new ProjectService();
        _editorService = new EditorService(_extensionProvider);
        _captionCatalog = CaptionCatalog.Compose(
            Beutl.Language.Strings.AiSubtitle_DefaultTemplate,
            Beutl.Editor.Services.ObjectTemplateService.Instance.FindByBaseType(
                typeof(Beutl.Graphics.Drawable)),
            _extensionProvider,
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
            _aiPlanCoordinator,
            _beutlClients.GetResource<IAiImageGenerationService>(),
            _beutlClients.GetResource<IAuthenticatedContentService>(),
            editViewModel);

    internal AiImageEditDialogViewModel CreateAiImageEditToolViewModel(EditViewModel editViewModel)
        => new(
            _beutlClients.GetResource<IAiEntitlementService>(),
            _beutlClients.GetResource<IAiOperationAvailabilityService>(),
            _aiPlanCoordinator,
            _beutlClients.GetResource<IAiImageEditingService>(),
            _beutlClients.GetResource<IAuthenticatedContentService>(),
            editViewModel);

    internal AiSubtitleDialogViewModel CreateAiSubtitleToolViewModel(EditViewModel? editViewModel)
    {
        _captionCatalog.RefreshObjectTemplates(
            Beutl.Editor.Services.ObjectTemplateService.Instance.FindByBaseType(
                typeof(Beutl.Graphics.Drawable)));
        return new AiSubtitleDialogViewModel(
            _beutlClients.GetResource<IAiEntitlementService>(),
            _beutlClients.GetResource<IAiOperationAvailabilityService>(),
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
            _aiPlanCoordinator,
            _beutlClients.GetResource<IAiVideoService>(),
            _beutlClients.GetResource<IAuthenticatedContentService>(),
            _beutlClients.GetResource<IAiJobKindRegistry>(),
            _beutlClients.GetResource<IAiJobMonitor>(),
            editViewModel);

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
            _projectService.CloseProject();
            await EditorHost.WaitForPendingProjectChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to close the active project during shutdown.");
        }

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

    internal void OpenAiJobCenter()
        => OpenAiToolTab(CreateAiJobCenterViewModel);

    internal void OpenAiImageGeneration()
        => OpenAiToolTab(CreateAiImageGenerationToolViewModel);

    internal void OpenAiImageEdit()
        => OpenAiToolTab(CreateAiImageEditToolViewModel);

    internal void OpenAiSubtitle(AiCaptionHistoryResult? historyResult = null)
    {
        AiSubtitleDialogViewModel? viewModel = OpenAiToolTab(CreateAiSubtitleToolViewModel);
        if (historyResult is not null)
        {
            viewModel?.LoadHistoryResult(historyResult);
        }
    }

    internal void OpenAiVideoGeneration()
        => OpenAiToolTab(CreateAiVideoGenerationToolViewModel);

    private T? OpenAiToolTab<T>(Func<EditViewModel, T> create)
        where T : class, IToolContext
    {
        if (_editorService.SelectedTabItem.Value?.Context.Value is not EditViewModel editorContext)
        {
            return null;
        }

        if (editorContext.FindToolTab<T>() is { } existing)
        {
            editorContext.OpenToolTab(existing);
            return existing;
        }

        T toolContext = create(editorContext);
        if (!editorContext.OpenToolTab(toolContext))
        {
            toolContext.Dispose();
            return null;
        }

        return toolContext;
    }

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
