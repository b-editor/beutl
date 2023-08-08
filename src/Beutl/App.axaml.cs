using System.Reflection;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using Avalonia.Styling;
using Avalonia.Threading;

using Beutl.Configuration;
using Beutl.NodeTree.Nodes;
using Beutl.Operators;
using Beutl.Rendering;
using Beutl.Services;
using Beutl.ViewModels;
using Beutl.Views;

using FluentAvalonia.Core;
using FluentAvalonia.Styling;

using Microsoft.Extensions.Logging;

using Reactive.Bindings;

using Serilog;

namespace Beutl;

public sealed class App : Application
{
    private FluentAvaloniaTheme? _theme;
    private MainViewModel? _mainViewModel;

    public override void Initialize()
    {
        SetupLogger();

        FAUISettings.SetAnimationsEnabledAtAppLevel(true);

        //PaletteColors
        Type colorsType = typeof(Colors);
        PropertyInfo[] colorProperties = colorsType.GetProperties(BindingFlags.Public | BindingFlags.GetProperty | BindingFlags.Static);
        Color[] colors = colorProperties.Select(p => p.GetValue(null)).OfType<Color>().ToArray();

        GlobalConfiguration config = GlobalConfiguration.Instance;
        config.Restore(GlobalConfiguration.DefaultFilePath);
        ViewConfig view = config.ViewConfig;
        CultureInfo.CurrentUICulture = view.UICulture;

        AvaloniaXamlLoader.Load(this);
        Resources["PaletteColors"] = colors;

        _theme = (FluentAvaloniaTheme)Styles[0];

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
            });
        });

#if DEBUG
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
        };
#endif
    }

    public override void RegisterServices()
    {
        base.RegisterServices();
        ServiceLocator.Current
            .BindToSelfSingleton<EditorService>()
            .BindToSelfSingleton<OutputService>()
            .BindToSelfSingleton<HttpClient>()
            .BindToSelfSingleton<ProjectService>()
            .Bind<IPropertyEditorExtensionImpl>().ToSingleton<PropertyEditorService.PropertyEditorExtensionImpl>()
            .BindToSelf<IProjectItemContainer>(new ProjectItemContainer())
            .BindToSelf<INotificationService>(new NotificationService());

        GetMainViewModel().RegisterServices();

        LibraryRegistrar.RegisterAll();
        NodesRegistrar.RegisterAll();
        ReactivePropertyScheduler.SetDefault(AvaloniaScheduler.Instance);
    }

    private void SetupLogger()
    {
        string logFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".beutl", "log", "log.txt");
        const string OutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext:l}] {Message:lj}{NewLine}{Exception}";
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
#if DEBUG
            .MinimumLevel.Debug()
            .WriteTo.Debug(outputTemplate: OutputTemplate)
#else
            .MinimumLevel.Debug()
#endif
            .WriteTo.File(logFile, rollingInterval: RollingInterval.Day, outputTemplate: OutputTemplate)
            .CreateLogger();

        BeutlApplication.Current.LoggerFactory = LoggerFactory.Create(builder => builder.AddSerilog(Log.Logger, true));
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _ = RenderThread.Dispatcher;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = GetMainViewModel(),
            };

            desktop.Exit += Application_Exit;
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

    private MainViewModel GetMainViewModel()
    {
        return _mainViewModel ??= new MainViewModel();
    }

    private void Application_Exit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        GlobalConfiguration.Instance.Save(GlobalConfiguration.DefaultFilePath);
        BeutlApplication.Current.LoggerFactory.Dispose();
    }
}
