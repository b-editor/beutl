using System.Globalization;
using System.Net.Http;

using Avalonia;

using BEditor.Models;

using Microsoft.Extensions.DependencyInjection;

namespace BEditor
{
    static class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        public static void Main(string[] args)
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);

            Settings.Default.Save();

            DirectoryManager.Default.Stop();

            App.BackupTimer.Stop();

            var app = AppModel.Current;

            app.ServiceProvider.GetService<HttpClient>()?.Dispose();

            app.Project?.Unload();
            app.Project = null;
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}
