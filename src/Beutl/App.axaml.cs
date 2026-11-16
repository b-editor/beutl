using System.Globalization;
using Avalonia;
#if DEBUG
using Avalonia.Diagnostics;
#endif
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Controls.NativeContextMenu;
using Beutl.Controls.Styling;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Graphics.Backend;
using Beutl.Helpers;
using Beutl.Logging;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.Pages;
using Beutl.Pages.SettingsPages;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.Services.StartupTasks;
using Beutl.Services.Tutorials;
using Beutl.ViewModels;
using Beutl.Views;
using FluentAvalonia.Core;
using FluentAvalonia.Styling;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl;

public sealed class App : Application
{
    private static readonly ILogger s_logger = Log.CreateLogger<App>();
    private readonly TaskCompletionSource _windowOpenTcs = new();
    private FluentAvaloniaTheme? _theme;
    private ThemeService? _themeService;
    private MainViewModel? _mainViewModel;
    private Startup? _startUp;
    private LoadSideloadExtensionTask? _sideloadExtensionTask;
    private readonly SemaphoreSlim _packageLinkGate = new(1, 1);
    private readonly CancellationTokenSource _activationCts = new();

    public override void Initialize()
    {
        using Activity? activity = Telemetry.StartActivity("App.Initialize");

        FAUISettings.SetAnimationsEnabledAtAppLevel(true);

        GlobalConfiguration config = GlobalConfiguration.Instance;
        ViewConfig view = config.ViewConfig;

        GraphicsContextFactory.SelectGpuByName(config.GraphicsConfig.SelectedGpuName.Value);

        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
        RegisterBundledEngineFonts();
        _startUp = GetMainViewModel().RunStartupTask();
        _sideloadExtensionTask = _startUp.GetTask<LoadSideloadExtensionTask>();
        _startUp.WaitAll().ContinueWith(_ => _startUp = null);
        ApplyDockStringOverrides();

        _theme = (FluentAvaloniaTheme)Styles[0];

        activity?.AddEvent(new ActivityEvent("Xaml_Loaded"));

        _themeService = new ThemeService(_theme!, view);
        _themeService.Start();

        if (!OperatingSystem.IsWindows())
        {
            _theme.RemoveRange(1, _theme.Count - 1);
        }
    }

    // The same fonts and order as the UI, shared with the standalone MCP server (Beutl.AgentToolkit).
    internal static void RegisterBundledEngineFonts()
    {
        Media.BundledFonts.Apply(Media.FontManager.Instance, CultureInfo.CurrentUICulture);
    }

    private void ApplyDockStringOverrides()
    {
        Resources["ToolChromeControlFloatString"] = DockStrings.ToolChromeControlFloatString;
        Resources["ToolChromeControlDockString"] = DockStrings.ToolChromeControlDockString;
        Resources["ToolChromeControlAutoHideString"] = DockStrings.ToolChromeControlAutoHideString;
        Resources["ToolChromeControlCloseString"] = DockStrings.ToolChromeControlCloseString;

        Resources["ToolTabStripItemFloatString"] = DockStrings.ToolTabStripItemFloatString;
        Resources["ToolTabStripItemFloatAllString"] = DockStrings.ToolTabStripItemFloatAllString;
        Resources["ToolTabStripItemDockString"] = DockStrings.ToolTabStripItemDockString;
        Resources["ToolTabStripItemAutoHideString"] = DockStrings.ToolTabStripItemAutoHideString;
        Resources["ToolTabStripItemCloseString"] = DockStrings.ToolTabStripItemCloseString;

        Resources["ToolPinItemControlFloatString"] = DockStrings.ToolPinItemControlFloatString;
        Resources["ToolPinItemControlShowString"] = DockStrings.ToolPinItemControlShowString;
        Resources["ToolPinItemControlCloseString"] = DockStrings.ToolPinItemControlCloseString;

        Resources["DragPreviewControlDockString"] = DockStrings.DragPreviewControlDockString;
        Resources["DragPreviewControlFloatString"] = DockStrings.DragPreviewControlFloatString;
        Resources["DragPreviewControlNoneString"] = DockStrings.DragPreviewControlNoneString;
    }

    public override void RegisterServices()
    {
        using Activity? activity = Telemetry.StartActivity("App.RegisterServices");

        base.RegisterServices();

        UiExceptionBoundary.Install(Dispatcher.UIThread);
        // After the shared hooks, whose notification handler the proxy store's warnings need.
        RegisterProcessServices(GetContextCommandManager, () =>
        {
            try
            {
                ProxyMediaServices.Initialize(GlobalConfiguration.Instance);
            }
            catch (Exception ex)
            {
                s_logger.LogWarning(ex, "Proxy media services failed to initialize.");
            }

            GetMainViewModel().RegisterServices();
        });
    }

    // The process-wide hooks and registrations that the app and the UI tests share.
    internal static void RegisterProcessServices(Func<ContextCommandManager?> commands, Action? alongside = null)
    {
        PropertyEditorControls.Install();
        NotificationService.Handler = new NotificationServiceHandler();
        TutorialService.Current = new TutorialServiceHandler();
        AppHelper.GetContextCommandManager = commands;
        GraphSnapshot.PostToDocumentThread = static write =>
        {
            if (Dispatcher.UIThread.CheckAccess())
                write();
            else
                Dispatcher.UIThread.Post(write);
        };

        // They are slow, so alongside runs with them, first: Parallel.Invoke runs that one on the calling thread.
        Action[] actions = alongside is null
            ? [LibraryRegistrar.RegisterAll, NodesRegistrar.RegisterAll]
            : [alongside, LibraryRegistrar.RegisterAll, NodesRegistrar.RegisterAll];
        Parallel.Invoke(actions);

        ReactivePropertyScheduler.SetDefault(UiThreadScheduler.Instance);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        MacOSKeyBindings.Install(this);
        ShowSettingsGestureInApplicationMenu();
        Program.ActivationBroker?.SetHandler(HandlePackageLink);
        if (this.TryGetFeature<IActivatableLifetime>() is { } activatableLifetime)
        {
            activatableLifetime.Activated += (_, args) =>
            {
                if (args is ProtocolActivatedEventArgs protocol)
                    HandlePackageLink(protocol.Uri.OriginalString);
            };
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (OperatingSystem.IsMacOS())
            {
                NativeContextMenus.Install();
                desktop.MainWindow = new MacWindow { DataContext = GetMainViewModel(), };
            }
            else
            {
                desktop.MainWindow = new MainWindow { DataContext = GetMainViewModel(), };
            }

            desktop.MainWindow.Opened += (_, _) => _windowOpenTcs.SetResult();
            desktop.Exit += (_, _) => _activationCts.Cancel();
            foreach (string argument in desktop.Args ?? [])
                HandlePackageLink(argument);
        }

        base.OnFrameworkInitializationCompleted();
    }

    internal void HandlePackageLink(string value)
    {
        if (PackageInstallRequest.TryParse(value, out PackageInstallRequest? request))
            Dispatcher.UIThread.Post(() => OpenPackageAsync(request));
    }

    private async void OpenPackageAsync(PackageInstallRequest request)
    {
        CancellationToken token = _activationCts.Token;
        bool entered = false;
        try
        {
            await _packageLinkGate.WaitAsync(token);
            entered = true;
            await _windowOpenTcs.Task.WaitAsync(token);
            // Authentication and extension registration must finish before creating the store models.
            await WaitStartupTask().AsTask().WaitAsync(token);
            if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow } desktop)
                return;

            this.TryGetFeature<IActivatableLifetime>()?.TryLeaveBackground();
            if (mainWindow.WindowState == WindowState.Minimized)
                mainWindow.WindowState = WindowState.Normal;
            mainWindow.Activate();

            MainViewModel main = GetMainViewModel();
            var package = await main._beutlClients.GetResource<DiscoverService>()
                .GetPackage(request.PackageName, token);
            token.ThrowIfCancellationRequested();

            ExtensionsPage? page = desktop.Windows.OfType<ExtensionsPage>().FirstOrDefault();
            if (page == null)
            {
                var viewModel = new ExtensionsPageViewModel(main._beutlClients, main.EditorService, main.ProjectService);
                page = new ExtensionsPage { DataContext = viewModel };
                page.Closed += (_, _) => viewModel.Dispose();
                page.OpenPackage(package, request.Version);
                _ = page.ShowDialog(mainWindow);
            }
            else
            {
                page.OpenPackage(package, request.Version);
                if (page.WindowState == WindowState.Minimized)
                    page.WindowState = WindowState.Normal;
                page.Activate();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            s_logger.LogError(e, "Failed to open package {PackageName} from the web store.", request.PackageName);
            await e.Handle();
        }
        finally
        {
            if (entered)
                _packageLinkGate.Release();
        }
    }

    public static ContextCommandManager? GetContextCommandManager()
    {
        return ((App)Current!).GetMainViewModel().ContextCommandManager;
    }

    public static FluentAvaloniaTheme? GetFATheme()
    {
        return (Current as App)?._theme;
    }

    public static async ValueTask WaitStartupTask()
    {
        if (Current is App { _startUp: { } obj })
        {
            await obj.WaitAll();
        }
    }

    public static async ValueTask WaitLoadingExtensions()
    {
        if (Current is App { _startUp: { } obj })
        {
            await obj.WaitLoadingExtensions();
        }
        else if (Current is App { _sideloadExtensionTask: { } sideloadTask })
        {
            await sideloadTask.WaitForAcceptedLoadingAsync();
        }
    }

    public static async ValueTask WaitWindowOpened()
    {
        if (Current is App app)
        {
            await app._windowOpenTcs.Task.ConfigureAwait(false);
        }
    }

    private MainViewModel GetMainViewModel()
    {
        return _mainViewModel ??= new MainViewModel();
    }

    private void AboutBeutlClicked(object? sender, EventArgs e)
    {
        _mainViewModel?.MenuBar.ShowInformation.Execute();
    }

    private void OpenSettingsClicked(object? sender, EventArgs e)
    {
        // AppKit gives the entry its key before the key map's flyout sees it, and the key is then the one being assigned.
        if (sender is NativeMenuItem { Gesture: { } gesture }
            && (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is { } window
            && FocusedEditCommand.IsCurrentEventKeyDown(window)
            && KeyMapFlyout.TakeMenuKey(gesture))
        {
            return;
        }

        _mainViewModel?.MenuBar.ShowSettings.Execute();
    }

    // The application menu's entry shows the command's gesture, which is known once the commands are registered, and
    // follows the key map after that.
    private void ShowSettingsGestureInApplicationMenu()
    {
        if (NativeMenu.GetMenu(this)?.Items.OfType<NativeMenuItem>().FirstOrDefault(item => item.Header == Strings.Settings) is not { } settings)
            return;

        void Show() => settings.Gesture = GetCommandGestureExtension.GetKeyGesture(
            typeof(MainViewExtension), MainViewExtension.ShowSettingsCommandId);

        Task registered = _startUp?.GetTask<LoadPrimitiveExtensionTask>().Task ?? Task.CompletedTask;
        registered.ContinueWith(
            _ => Dispatcher.UIThread.Post(() =>
            {
                Show();
                if (AppHelper.GetContextCommandManager?.Invoke() is { } commands)
                    commands.GesturesChanged += (_, _) => Dispatcher.UIThread.Post(Show);
            }),
            TaskScheduler.Default);
    }
}
