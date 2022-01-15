using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using BeUtl.Framework.Service;
using BeUtl.Operations;
using BeUtl.Services;
using BeUtl.ViewModels;
using BeUtl.Views;

using Reactive.Bindings;

namespace BeUtl
{
    public class App : Application
    {
        public override void Initialize()
        {
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
                    DataContext = new MainWindowViewModel(),
                };

                desktop.Exit += Application_Exit;
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void Application_Exit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
        {
            SceneRenderer.s_dispatcher.Stop();
        }
    }
}
