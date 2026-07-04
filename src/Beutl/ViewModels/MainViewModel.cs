using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Beutl.AgentHost;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Helpers;
using Beutl.Services;
using Beutl.Services.StartupTasks;
using Beutl.ViewModels.ExtensionsPages;
using DynamicData;
using DynamicData.Binding;
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
    private readonly AgentHostEndpoint _agentHostEndpoint;

    public MainViewModel()
    {
        _authHttpClient = new HttpClient();
        // Composition root: own the editor-session services here and thread the instances
        // down to child view models and services.
        _extensionProvider = new ExtensionProvider();
        _projectService = new ProjectService();
        _editorService = new EditorService(_extensionProvider);
        _agentHostEndpoint = new AgentHostEndpoint(_projectService, _editorService);
        _beutlClients = new BeutlApiApplication(_authHttpClient, _extensionProvider);
        ContextCommandManager = _beutlClients.GetResource<ContextCommandManager>();

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

        _ = _agentHostEndpoint.StartAsync();
    }

    public override void Dispose()
    {
        CommandPalette.Dispose();
        _agentHostEndpoint.RequestStop();
        _projectService.CloseProject();
        BeutlApplication.Current.Items.Clear();
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        _agentHostEndpoint.RequestStop();

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
