using System.Runtime;

using Avalonia;
using Avalonia.Media;
using Avalonia.ReactiveUI;

using Beutl.Configuration;
using Beutl.Rendering;
using Beutl.Services;

using Microsoft.Extensions.Logging;

using Serilog;

namespace Beutl;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Restore config
        GlobalConfiguration config = GlobalConfiguration.Instance;
        config.Restore(GlobalConfiguration.DefaultFilePath);

        using IDisposable _ = Telemetry.GetDisposable();
        Telemetry.Started();

        // PGOを有効化
        string jitProfiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".beutl", "jitProfiles");
        if (!Directory.Exists(jitProfiles))
            Directory.CreateDirectory(jitProfiles);

        ProfileOptimization.SetProfileRoot(jitProfiles);
        ProfileOptimization.StartProfile("beutl.jitprofile");

        WaitForExitOtherProcesses();

        SetupLogger();
        Log.Information("After setup logger");

        UnhandledExceptionHandler.Initialize();

        RenderThread.Dispatcher.Dispatch(SharedGPUContext.Create, Threading.DispatchPriority.High);

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

        // 正常に終了した
        UnhandledExceptionHandler.Exit();
    }

    private static void WaitForExitOtherProcesses()
    {
        Process[] processes = Process.GetProcessesByName("Beutl.PackageTools");
        if (processes.Length > 0)
        {
            var startInfo = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Beutl.WaitingDialog"))
            {
                ArgumentList =
                {
                    "--title", Message.OpeningBeutl,
                    "--subtitle", Message.Changes_to_the_package_are_in_progress,
                    "--content", Message.To_open_Beutl_close_Beutl_PackageTools,
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
    }

    public static AppBuilder BuildAvaloniaApp()
    {
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
#if DEBUG
            .LogToTrace();
#else
            ;
#endif
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
