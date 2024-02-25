using System.Reactive.Concurrency;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using Avalonia.Styling;
using Avalonia.Threading;

using Beutl.Configuration;
using Beutl.NodeTree.Nodes;
using Beutl.Operators;
using Beutl.Services;
using Beutl.Services.StartupTasks;
using Beutl.ViewModels;
using Beutl.Views;

using FluentAvalonia.Core;
using FluentAvalonia.Styling;

using Reactive.Bindings;

namespace Beutl;

public sealed class App : Application
{
    private readonly TaskCompletionSource _windowOpenTcs = new();
    private FluentAvaloniaTheme? _theme;
    private MainViewModel? _mainViewModel;
    private Startup? _startUp;

    public override void Initialize()
    {
        _startUp = GetMainViewModel().RunStartupTask();
        _startUp.WaitAll().ContinueWith(_ => _startUp = null);

        using Activity? activity = Telemetry.StartActivity("App.Initialize");

        FAUISettings.SetAnimationsEnabledAtAppLevel(true);

        GlobalConfiguration config = GlobalConfiguration.Instance;
        ViewConfig view = config.ViewConfig;

        AvaloniaXamlLoader.Load(this);
        Resources["PaletteColors"] = AppHelpers.GetPaletteColors();

        _theme = (FluentAvaloniaTheme)Styles[0];

        activity?.AddEvent(new ActivityEvent("Xaml_Loaded"));

        view.GetObservable(ViewConfig.ThemeProperty).Subscribe(v =>
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                switch (v)
                {
                    case ViewConfig.ViewTheme.Light:
                        RequestedThemeVariant = ThemeVariant.Light;
                        break;
                    case ViewConfig.ViewTheme.Dark:
                        RequestedThemeVariant = ThemeVariant.Dark;
                        break;
                    case ViewConfig.ViewTheme.HighContrast:
                        RequestedThemeVariant = FluentAvaloniaTheme.HighContrastTheme;
                        break;
                    case ViewConfig.ViewTheme.System:
                        _theme.PreferSystemTheme = true;
                        break;
                }
            }, DispatcherPriority.Send);
        });

        if (view.UseCustomAccentColor && Color.TryParse(view.CustomAccentColor, out Color customColor))
        {
            activity?.SetTag("CustomAccentColor", customColor.ToString());

            _theme.CustomAccentColor = customColor;
        }
    }

    public override void RegisterServices()
    {
        using Activity? activity = Telemetry.StartActivity("App.RegisterServices");

        base.RegisterServices();

        PropertyEditorExtension.DefaultHandler = new PropertyEditorService.PropertyEditorExtensionImpl();
        ProjectItemContainer.Current.Generator = new ProjectItemGenerator();
        NotificationService.Handler = new NotificationServiceHandler();

        // 以下三つの処理は意外と重い
        Parallel.Invoke(
            () => GetMainViewModel().RegisterServices(),
            LibraryRegistrar.RegisterAll,
            NodesRegistrar.RegisterAll);

        ReactivePropertyScheduler.SetDefault(ImmediateScheduler.Instance);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = GetMainViewModel(),
            };

            desktop.MainWindow.Opened += (_, _) => _windowOpenTcs.SetResult();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = GetMainViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static IClipboard? GetClipboard()
    {
        return GetTopLevel()?.Clipboard;
    }

    public static TopLevel? GetTopLevel()
    {
        return (Current?.ApplicationLifetime) switch
        {
            IClassicDesktopStyleApplicationLifetime desktop => desktop.MainWindow,
            ISingleViewApplicationLifetime { MainView: { } mainview } => TopLevel.GetTopLevel(mainview),
            _ => null,
        };
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
}
