using System.Reflection;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;

using Beutl.Configuration;
using Beutl.Framework;
using Beutl.Framework.Service;
using Beutl.Framework.Services;
using Beutl.Operators;
using Beutl.ProjectSystem;
using Beutl.Rendering;
using Beutl.Services;
using Beutl.ViewModels;
using Beutl.Views;

using FluentAvalonia.Styling;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace Beutl;

public sealed class App : Application
{
    private readonly Uri _baseUri = new("avares://Beutl/App.axaml");
    private IStyle? _cultureStyle;
    private MainViewModel? _mainViewModel;

    public override void Initialize()
    {
        //PaletteColors
        Type colorsType = typeof(Colors);
        PropertyInfo[] colors = colorsType.GetProperties(BindingFlags.Public | BindingFlags.GetProperty | BindingFlags.Static);
        Resources["PaletteColors"] = colors.Select(p => p.GetValue(null)).OfType<Color>().ToArray();

        GlobalConfiguration config = GlobalConfiguration.Instance;
        config.Restore(GlobalConfiguration.DefaultFilePath);

        AvaloniaXamlLoader.Load(this);

        ViewConfig view = config.ViewConfig;
        view.GetObservable(ViewConfig.ThemeProperty).Subscribe(v =>
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                FluentAvaloniaTheme thm = AvaloniaLocator.Current.GetService<FluentAvaloniaTheme>()!;
                switch (v)
                {
                    case ViewConfig.ViewTheme.Light:
                        thm.RequestedTheme = FluentAvaloniaTheme.LightModeString;
                        break;
                    case ViewConfig.ViewTheme.Dark:
                        thm.RequestedTheme = FluentAvaloniaTheme.DarkModeString;
                        break;
                    case ViewConfig.ViewTheme.HighContrast:
                        thm.RequestedTheme = FluentAvaloniaTheme.HighContrastModeString;
                        break;
                    case ViewConfig.ViewTheme.System:
                        thm.PreferSystemTheme = true;
                        thm.InvalidateThemingFromSystemThemeChanged();
                        break;
                }
            });
        });

        view.GetObservable(ViewConfig.UICultureProperty).Subscribe(v =>
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (LocalizeService.Instance.IsSupportedCulture(v) || v.Name == "en-US")
                {
                    IStyle? tmp = _cultureStyle;
                    _cultureStyle = new StyleInclude(_baseUri)
                    {
                        Source = LocalizeService.Instance.GetUri(v)
                    };
                    Styles.Add(_cultureStyle);
                    if (tmp != null)
                    {
                        Styles.Remove(tmp);
                    }
                }
                else
                {
                    if (_cultureStyle != null)
                    {
                        Styles.Remove(_cultureStyle);
                    }

                    _cultureStyle = null;
                }

                CultureInfo.CurrentUICulture = v;
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
        //AvaloniaLocator.CurrentMutable
        //        .Bind<IFontManagerImpl>().ToConstant(new CustomFontManagerImpl());

        ServiceLocator.Current
            .BindToSelfSingleton<EditorService>()
            .BindToSelfSingleton<HttpClient>()
            .Bind<IPropertyEditorExtensionImpl>().ToSingleton<PropertyEditorService.PropertyEditorExtensionImpl>()
            .BindToSelf<IWorkspaceItemContainer>(new WorkspaceItemContainer())
            .BindToSelf<IProjectService>(new ProjectService())
            .BindToSelf<INotificationService>(new NotificationService())
            .BindToSelf<IResourceProvider>(new DefaultResourceProvider());

        GetMainViewModel().RegisterServices();

        OperatorsRegistrar.RegisterAll();
        UIDispatcherScheduler.Initialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _ = DeferredRenderer.s_dispatcher;
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
        DeferredRenderer.s_dispatcher.Stop();
    }
}
