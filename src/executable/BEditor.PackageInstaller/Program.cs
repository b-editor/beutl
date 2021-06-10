using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;

using Avalonia;

using BEditor.Packaging;

namespace BEditor.PackageInstaller
{
    class Program
    {
        [AllowNull]
        public static string JsonFile;

        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        public static void Main(string[] args)
        {
            if (args.Length is 0 || Path.GetExtension(args.FirstOrDefault()) is not ".json") return;
            if (!File.Exists(args[0])) return;

            JsonFile = args[0];

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}