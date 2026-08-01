using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Beutl.AgentHost;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Editor.Components.VersionControl.ViewModels;
using Beutl.Helpers;
using Beutl.Logging;
using Beutl.Services;
using Beutl.Services.StartupTasks;
using Beutl.ViewModels.ExtensionsPages;
using DynamicData;
using DynamicData.Binding;
using Microsoft.Extensions.Logging;
using NuGet.Packaging.Core;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class MainViewModel : BasePageViewModel, IContextCommandHandler, IAsyncDisposable
{
    internal readonly BeutlApiApplication _beutlClients;
    private readonly HttpClient _authHttpClient;
    private readonly ProjectService _projectService;
    private readonly EditorService _editorService;
    private readonly VersionControlCoordinator _versionControlCoordinator;
    private readonly ExtensionProvider _extensionProvider;
    private readonly AgentHostEndpoint _agentHostEndpoint;
    private readonly ILogger _logger = Log.CreateLogger<MainViewModel>();
    private readonly object _shutdownGate = new();
    private readonly TaskCompletionSource _disposalCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _shutdownTask;
    private int _disposed;

    public MainViewModel()
        : this(static (projectService, editorService) =>
            new AgentHostEndpoint(projectService, editorService))
    {
    }

    internal MainViewModel(
        Func<ProjectService, EditorService, AgentHostEndpoint> agentHostFactory)
    {
        ArgumentNullException.ThrowIfNull(agentHostFactory);

        _authHttpClient = new HttpClient();
        // Composition root: own the editor-session services here and thread the instances
        // down to child view models and services.
        _extensionProvider = new ExtensionProvider();
        _projectService = new ProjectService();
        _editorService = new EditorService(_extensionProvider);
        _versionControlCoordinator = new VersionControlCoordinator(_projectService, _editorService);
        _agentHostEndpoint = agentHostFactory(_projectService, _editorService);
        _beutlClients = new BeutlApiApplication(_authHttpClient, _extensionProvider);
        ContextCommandManager = _beutlClients.GetResource<ContextCommandManager>();

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
        return new SettingsDialogViewModel(_beutlClients, _extensionProvider, _agentHostEndpoint);
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
            lifetime.Exit += OnExit;
        }

        _agentHostEndpoint.StartInBackground();
    }

    public override void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _agentHostEndpoint.RequestStop();
        _ = CompleteDisposalAsync();
        _ = ObserveDisposalCompletionAsync();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return new ValueTask(_disposalCompletion.Task);
    }

    private async Task CompleteDisposalAsync()
    {
        var failures = new List<Exception>();
        try
        {
            CommandPalette.Dispose();
        }
        catch (Exception ex)
        {
            failures.Add(ex);
        }

        try
        {
            TitleBarBranch.Dispose();
        }
        catch (Exception ex)
        {
            failures.Add(ex);
        }

        try
        {
            await _agentHostEndpoint.DisposeAsync();
        }
        catch (Exception ex)
        {
            failures.Add(ex);
        }

        try
        {
            await Task.WhenAll(
                EditorHost.DisposeAsync().AsTask(),
                _versionControlCoordinator.DisposeAsync().AsTask());
        }
        catch (Exception ex)
        {
            failures.Add(ex);
        }

        try
        {
            BeutlApplication.Current.Items.Clear();
        }
        catch (Exception ex)
        {
            failures.Add(ex);
        }

        if (failures.Count == 0)
        {
            _disposalCompletion.TrySetResult();
        }
        else
        {
            _disposalCompletion.TrySetException(
                failures.Count == 1 ? failures[0] : new AggregateException(failures));
        }
    }

    private async Task ObserveDisposalCompletionAsync()
    {
        try
        {
            await _disposalCompletion.Task;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispose the main view-model composition root.");
        }
    }

    internal Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        lock (_shutdownGate)
        {
            return _shutdownTask ??= ShutdownCoreAsync(cancellationToken);
        }
    }

    private async Task ShutdownCoreAsync(CancellationToken cancellationToken)
    {
        // Publish the single-flight task before a synchronous Closing handler can re-enter shutdown.
        await Task.Yield();
        _agentHostEndpoint.RequestStop();
        _projectService.RequestShutdown();

        try
        {
            try
            {
                // AgentHost owns the ingress to ProjectService and EditorService. Drain active MCP
                // requests before closing the project or disposing either dependency.
                await _agentHostEndpoint.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to drain the agent host during application shutdown.");
            }

            try
            {
                await using ProjectService.ProjectTransitionScope transition =
                    await _projectService.BeginShutdownTransitionAsync(this, cancellationToken);
                // The shutdown transition waits for an active version-control mutation to finish
                // its recovery and reopen before the final close runs.
                await transition.CloseProjectAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to close the project during application shutdown.");
            }

            if (ProxyMediaServices.Current is { } proxyMediaServices)
            {
                Task disposalTask = proxyMediaServices.DisposeAsync().AsTask();
                try
                {
                    await disposalTask;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Proxy media services failed to dispose during shutdown.");
                }
            }
        }
        finally
        {
            await DisposeAsync();
        }
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        _agentHostEndpoint.RequestStop();
        if (ProxyMediaServices.Current is { } proxyMediaServices)
        {
            // The window pipeline normally drains this before closing. If the deadline expired (or
            // another lifetime initiated exit), make a best-effort start without blocking the UI thread.
            _ = DisposeProxyMediaServicesAfterExitAsync(proxyMediaServices);
        }

        PackageChangesQueue queue = _beutlClients.GetResource<PackageChangesQueue>();
        PackageIdentity[] installs = queue.GetInstalls().ToArray();
        PackageIdentity[] uninstalls = queue.GetUninstalls().ToArray();

        if (installs.Length > 0 || uninstalls.Length > 0)
        {
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
    }

    private async Task DisposeProxyMediaServicesAfterExitAsync(ProxyMediaServices proxyMediaServices)
    {
        try
        {
            await proxyMediaServices.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Proxy media services failed to dispose during shutdown.");
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
