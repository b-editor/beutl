using System;
using System.IO;
using System.Linq;

using Avalonia;

using BEditor.Packaging;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace BEditor.PackageInstaller
{
    class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        public static void Main(string[] args)
        {
            var file = @"C:\github.com\BEditor.Extensions.FFmpeg.bepkg";
            PackageFile.CreatePackage(
                @"C:\github.com\b-editor\BEditor\src\BEditor.Avalonia\bin\Debug\net5.0\user\plugins\BEditor.Extensions.FFmpeg\BEditor.Extensions.FFmpeg.dll",
                file,
                new());
            PackageFile.OpenPackage(file, @"C:\github.com\BEditor.Extensions.FFmpeg");
#if DEBUG
            args = new string[] { Path.Combine(Environment.CurrentDirectory, "sample.json") };
#endif
            if (args.Length is 0 || Path.GetExtension(args.FirstOrDefault()) is not ".json") return;
            if (!File.Exists(args[0])) return;

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