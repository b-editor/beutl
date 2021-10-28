using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Resources;

using Avalonia;

using BEditor.LangResources;

namespace BEditor
{
    static class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            CultureInfo.CurrentUICulture = Settings.Default.Language.Culture;
            SetResourceManager(Settings.Default.Language);

            if (args.ElementAtOrDefault(0) == "package-install")
            {
                PackageInstaller.Program.Main(args.Skip(1).ToArray());
            }
            else
            {
                BuildAvaloniaApp()
                    .StartWithClassicDesktopLifetime(args);
            }
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .With(new X11PlatformOptions
                {
                    EnableIme = true,
                })
                .UsePlatformDetect()
                .LogToTrace();

        private static void SetResourceManager(SupportedLanguage supportedLanguage)
        {
            var manager = new ResourceManager(supportedLanguage.BaseName, supportedLanguage.Assembly);
            var field = typeof(Strings).GetField("resourceMan", BindingFlags.NonPublic | BindingFlags.SetField | BindingFlags.Static);

            field?.SetValue(null, manager);
        }
    }
}