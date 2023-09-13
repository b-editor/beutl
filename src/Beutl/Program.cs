using System.Runtime;

using Avalonia;
using Avalonia.Media;
using Avalonia.ReactiveUI;

using Azure.Monitor.OpenTelemetry.Exporter;

using Beutl.Configuration;
using Beutl.Rendering;
using Beutl.Services;

using Microsoft.Extensions.Logging;

using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

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

        using TracerProvider? tracerProvider = SetupTelemetry(config);
        using Activity? _ = Telemetry.StartActivity();

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

    private static TracerProvider? SetupTelemetry(GlobalConfiguration config)
    {
        TelemetryConfig t = config.TelemetryConfig;
        var list = new List<string>(4);
        if (t.Beutl_Application == true)
            list.Add("Beutl.Application");

        if (t.Beutl_ViewTracking == true)
            list.Add("Beutl.ViewTracking");

        if (t.Beutl_PackageManagement == true)
            list.Add("Beutl.PackageManagement");

        if (t.Beutl_Api_Client == true)
            list.Add("Beutl.Api.Client");

        if (list.Count == 0)
        {
            return null;
        }
        else
        {
            return Sdk.CreateTracerProviderBuilder()
                .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Beutl"))
                .AddSource(list.ToArray())
                //.AddZipkinExporter()
                .AddAzureMonitorTraceExporter(b => b.ConnectionString = "InstrumentationKey=b8cc7df1-1367-41f5-a819-5c95a10075cb")
                .Build();
        }
    }
}
