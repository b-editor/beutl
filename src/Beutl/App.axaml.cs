using System.Reflection;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;

using Beutl.Configuration;
using Beutl.Framework;
using Beutl.Framework.Service;
using Beutl.Framework.Services;
using Beutl.NodeTree.Nodes;
using Beutl.Operators;
using Beutl.Rendering;
using Beutl.Services;
using Beutl.ViewModels;
using Beutl.Views;

using FluentAvalonia.Core;
using FluentAvalonia.Styling;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace Beutl;

public sealed class App : Application
{
    private MainViewModel? _mainViewModel;

    public override void Initialize()
    {
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

#if DEBUG
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
        };
#endif
    }

    public override void RegisterServices()
    {
        base.RegisterServices();
        AvaloniaLocator.CurrentMutable
            .Bind<IFontManagerImpl>().ToConstant(new CustomFontManagerImpl());

        ServiceLocator.Current
            .BindToSelfSingleton<EditorService>()
            .BindToSelfSingleton<OutputService>()
            .BindToSelfSingleton<HttpClient>()
            .BindToSelfSingleton<BeutlApplication>()
            .Bind<IPropertyEditorExtensionImpl>().ToSingleton<PropertyEditorService.PropertyEditorExtensionImpl>()
            .BindToSelf<IProjectItemContainer>(new ProjectItemContainer())
            .BindToSelf<IProjectService>(new ProjectService())
            .BindToSelf<INotificationService>(new NotificationService());

        GetMainViewModel().RegisterServices();

        OperatorsRegistrar.RegisterAll();
        NodesRegistrar.RegisterAll();
        ReactivePropertyScheduler.SetDefault(AvaloniaScheduler.Instance);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _ = ImmediateRenderer.s_dispatcher;
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
        ImmediateRenderer.s_dispatcher.Stop();
    }
}
