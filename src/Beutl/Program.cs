using Avalonia;
using Avalonia.Media;
using Avalonia.ReactiveUI;

using Beutl.Services;

using Microsoft.Extensions.Logging;

using Serilog;

namespace Beutl;

internal static class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // STAThread属性がついている時に 'async Task Main' にするとDrag and Dropが動作しなくなる。
        Process[] processes = Process.GetProcessesByName("Beutl.PackageTools");
        if (processes.Length > 0)
        {
            var startInfo = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Beutl.WaitingDialog"))
            {
                ArgumentList =
                {
                    "--title", "Opening Beutl.",
                    "--subtitle", "Changes to the package are in progress.",
                    "--content", "To open Beutl, close Beutl.PackageTools.",
                    "--icon", "Info",
                    "--progress"
                }
            };

            var process = Process.Start(startInfo);

            foreach (Process item in processes)
            {
                if (!item.HasExited)
                {
                    item.WaitForExit();
                }
            }

            process?.Kill();
        }

        SetupLogger();

        UnhandledExceptionHandler.Initialize();

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

        // 正常に終了した
        UnhandledExceptionHandler.Exit();
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
#if DEBUG
        GC.KeepAlive(typeof(Avalonia.Svg.Skia.SvgImageExtension).Assembly);
        GC.KeepAlive(typeof(Avalonia.Svg.Skia.Svg).Assembly);
        GC.KeepAlive(typeof(FluentIcons.FluentAvalonia.SymbolIcon).Assembly);
        GC.KeepAlive(typeof(FluentIcons.Common.Symbol).Assembly);
        GC.KeepAlive(typeof(AsyncImageLoader.ImageLoader).Assembly);
#endif

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseReactiveUI()
            .With(new Win32PlatformOptions()
            {
                WinUICompositionBackdropCornerRadius = 8f
            })
            .With(new FontManagerOptions
            {
                DefaultFamilyName = Media.FontManager.Instance.DefaultTypeface.FontFamily.Name
            })
            .LogToTrace();
    }

    private static void SetupLogger()
    {
        string logFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".beutl", "log", "log.txt");
        const string OutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext:l}] {Message:lj}{NewLine}{Exception}";
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
#if DEBUG
            .MinimumLevel.Verbose()
            .WriteTo.Debug(outputTemplate: OutputTemplate)
#else
            .MinimumLevel.Debug()
#endif
            .WriteTo.Async(b => b.File(logFile, outputTemplate: OutputTemplate, shared: true, rollingInterval: RollingInterval.Day))
            .CreateLogger();

        BeutlApplication.Current.LoggerFactory = LoggerFactory.Create(builder => builder.AddSerilog(Log.Logger, true));
    }
}
