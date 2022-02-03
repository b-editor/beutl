using System.Reflection;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

using BeUtl.Framework.Service;
using BeUtl.Operations;
using BeUtl.Rendering;
using BeUtl.Services;
using BeUtl.ViewModels;
using BeUtl.Views;

using Reactive.Bindings;

namespace BeUtl;

public class App : Application
{
    public override void Initialize()
    {
        //PaletteColors
        Type colorsType = typeof(Colors);
        PropertyInfo[] colors = colorsType.GetProperties(BindingFlags.Public | BindingFlags.GetProperty | BindingFlags.Static);
        Resources["PaletteColors"] = colors.Select(p => p.GetValue(null)).OfType<Color>().ToArray();

        AvaloniaXamlLoader.Load(this);
    }

    public override void RegisterServices()
    {
        base.RegisterServices();
        ServiceLocator.Current.BindToSelfSingleton<ProjectService>()
            .Bind<INotificationService>().ToSingleton<NotificationService>()
            .Bind<IResourceProvider>().ToSingleton<DefaultResourceProvider>();

        RenderOperations.RegisterAll();
        UIDispatcherScheduler.Initialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(),
            };

            desktop.Exit += Application_Exit;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = new MainViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void Application_Exit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        DeferredRenderer.s_dispatcher.Stop();
    }
}
