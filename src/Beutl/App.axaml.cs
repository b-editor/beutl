using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Beutl.Editor.Components.FileBrowserTab.Services;
using Beutl.Media.Decoding;
#if DEBUG
using Avalonia.Diagnostics;
#endif
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Controls.NativeContextMenu;
using Beutl.Controls.PropertyEditors;
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
using LibraryService = Beutl.Services.LibraryService;

namespace Beutl;

public sealed class App : Application
{
    private static readonly ILogger s_logger = Log.CreateLogger<App>();
    private readonly TaskCompletionSource _windowOpenTcs = new();
    private FluentAvaloniaTheme? _theme;
    private ThemeService? _themeService;
    private MainViewModel? _mainViewModel;
    private readonly LibraryService _libraryService = new();
    private readonly ObjectTemplateService _objectTemplateService;
    private readonly ITutorialService _tutorialService;
    private readonly GlobalConfiguration _configuration;
    private readonly Media.FontManager _fonts;
    private readonly DecoderRegistry _decoders;
    private readonly INotificationService _notifications = new NotificationService(new NotificationServiceHandler());
    private readonly AppUpdateController _appUpdateController;
    private readonly ColorPaletteService _colorPalettes = ColorPaletteService.CreateDefault();
    private readonly EditorServiceRegistry _appServices;
    private readonly string _sessionId;
    private ProxyMediaServices? _proxyMediaServices;
    private Startup? _startUp;
    private LoadSideloadExtensionTask? _sideloadExtensionTask;
    private readonly SemaphoreSlim _beutlLinkGate = new(1, 1);
    private readonly CancellationTokenSource _activationCts = new();

    public App() : this(new GlobalConfiguration(), string.Empty)
    {
    }

    internal App(GlobalConfiguration configuration, string sessionId)
    {
        _configuration = configuration;
        _tutorialService = new TutorialServiceHandler(configuration.TutorialConfig);
        _appUpdateController = new AppUpdateController(configuration.UpdateConfig, _notifications);
        _decoders = new DecoderRegistry(configuration.ExtensionConfig);
        _fonts = new Media.FontManager(configuration.FontConfig);
        _objectTemplateService = new ObjectTemplateService(new SceneRenderServices(_fonts, _decoders, configuration.EditorConfig, configuration.ProxyStoreConfig));
        _sessionId = sessionId;
        _appServices = new EditorServiceRegistry()
            .Add(() => _configuration)
            .Add(() => _fonts)
            .Add(() => _decoders)
            .Add(() => _notifications)
            .Add(() => _appUpdateController)
            .Add(() => _libraryService)
            .Add(() => _objectTemplateService)
            .AddOwned(() => new FileThumbnailService(_decoders, _objectTemplateService))
            .Add<ITutorialService>(() => _tutorialService)
            .Add(() => _colorPalettes)
            .Add<ProxyMediaServices>(() => _proxyMediaServices);
    }

    public override void Initialize()
    {
        using Activity? activity = Telemetry.StartActivity("App.Initialize");

        FAUISettings.SetAnimationsEnabledAtAppLevel(true);

        GlobalConfiguration config = _configuration;
        ViewConfig view = config.ViewConfig;

        GraphicsContextFactory.SelectGpuByName(config.GraphicsConfig.SelectedGpuName.Value);

        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
        RegisterBundledEngineFonts(_fonts);
        _startUp = GetMainViewModel().RunStartupTask();
        _sideloadExtensionTask = _startUp.GetTask<LoadSideloadExtensionTask>();
        _startUp.WaitAll().ContinueWith(_ => _startUp = null);
        ApplyDockStringOverrides();

        _theme = (FluentAvaloniaTheme)Styles[0];

        activity?.AddEvent(new ActivityEvent("Xaml_Loaded"));

        _themeService = new ThemeService(_theme!, view, new ExtensionContext(_appServices));
        _themeService.Start();

        if (!OperatingSystem.IsWindows())
        {
            _theme.RemoveRange(1, _theme.Count - 1);
        }
    }

    // The same fonts and order as the UI, shared with the standalone MCP server (Beutl.AgentToolkit).
    internal static void RegisterBundledEngineFonts(Media.FontManager fonts)
    {
        Media.BundledFonts.Apply(fonts, CultureInfo.CurrentUICulture);
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

        UiExceptionBoundary.Install(Dispatcher.UIThread, _notifications);
        RegisterProcessServices(_libraryService, GetContextCommandManager, () =>
        {
            try
            {
                _proxyMediaServices = new ProxyMediaServices(_configuration, _notifications, _decoders);
            }
            catch (Exception ex)
            {
                s_logger.LogWarning(ex, "Proxy media services failed to initialize.");
            }

            GetMainViewModel().RegisterServices();
        });
    }

    // The process-wide hooks and registrations that the app and the UI tests share.
    internal static void RegisterProcessServices(LibraryService libraryService, Func<ContextCommandManager?> commands, Action? alongside = null)
    {
        PropertyEditorControls.Install();
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
            ? [() => LibraryRegistrar.RegisterAll(libraryService), NodesRegistrar.RegisterAll]
            : [alongside, () => LibraryRegistrar.RegisterAll(libraryService), NodesRegistrar.RegisterAll];
        Parallel.Invoke(actions);

        ReactivePropertyScheduler.SetDefault(UiThreadScheduler.Instance);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        MacOSKeyBindings.Install(this);
        ShowSettingsGestureInApplicationMenu();
        Program.ActivationBroker?.SetHandler(HandleBeutlLink);
        if (this.TryGetFeature<IActivatableLifetime>() is { } activatableLifetime)
        {
            activatableLifetime.Activated += (_, args) =>
            {
                if (args is ProtocolActivatedEventArgs protocol)
                    HandleBeutlLink(protocol.Uri.OriginalString);
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

            HostServices.SetFrom(desktop.MainWindow, _appServices);
            desktop.MainWindow.Opened += (_, _) => _windowOpenTcs.SetResult();
            desktop.Exit += (_, _) =>
            {
                _activationCts.Cancel();
                _appServices.Dispose();
                _objectTemplateService.Dispose();
                _libraryService.Dispose();
            };
            foreach (string argument in desktop.Args ?? [])
                HandleBeutlLink(argument);
        }

        base.OnFrameworkInitializationCompleted();
    }

    internal void HandleBeutlLink(string value)
    {
        if (BeutlLinkRequest.TryParse(value, out BeutlLinkRequest? request))
            Dispatcher.UIThread.Post(() => OpenBeutlLinkAsync(request));
    }

    private async void OpenBeutlLinkAsync(BeutlLinkRequest request)
    {
        CancellationToken token = _activationCts.Token;
        bool entered = false;
        try
        {
            await _beutlLinkGate.WaitAsync(token);
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
            if (request is RepositoryOpenRequest repository)
                await main.RepositoryClone.OpenAsync(mainWindow, repository, token);
            else if (request is PackageInstallRequest package)
                await OpenPackageAsync(package, desktop, mainWindow, main, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            s_logger.LogError(e, "Failed to open a Beutl protocol link.");
            await e.Handle(_notifications);
        }
        finally
        {
            if (entered)
                _beutlLinkGate.Release();
        }
    }

    private async Task OpenPackageAsync(PackageInstallRequest request,
        IClassicDesktopStyleApplicationLifetime desktop, Window mainWindow, MainViewModel main, CancellationToken token)
    {
        var package = await main._beutlClients.GetResource<DiscoverService>()
            .GetPackage(request.PackageName, token);
        token.ThrowIfCancellationRequested();

        ExtensionsPage? page = desktop.Windows.OfType<ExtensionsPage>().FirstOrDefault();
        if (page == null)
        {
            var viewModel = new ExtensionsPageViewModel(main._beutlClients, main.EditorService, main.ProjectService);
            page = new ExtensionsPage { DataContext = viewModel };
            HostServices.SetFrom(page, _appServices);
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
        return _mainViewModel ??= new MainViewModel(_appServices, _sessionId);
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
        if (NativeMenu.GetMenu(this)?.Items.OfType<NativeMenuItem>().FirstOrDefault(item => item.Header == Strings.OpenSettingsMenu) is not { } settings)
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
