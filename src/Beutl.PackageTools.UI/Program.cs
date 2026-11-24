using Avalonia;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Controls.Styling;
using Beutl.Logging;
using Beutl.Services;

namespace Beutl.PackageTools.UI;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        string? GetSessionId()
        {
            int idx = Array.IndexOf(args, "--session-id");
            if (idx >= 0 && idx + 1 < args.Length)
            {
                return args[idx + 1];
            }
            else
            {
                return null;
            }
        }

        // Restore config
        var config = new GlobalConfiguration();
        config.Restore(GlobalConfiguration.DefaultFilePath);

        using IDisposable _ = new Telemetry(config.TelemetryConfig, GetSessionId());
        config.LogRestoreFailures();
        ILogger<Program> logger = Log.CreateLogger<Program>();

        try
        {
            PackageInstaller.RecoverDataPackageInstalls();
            BuildAvaloniaApp(config)
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "An unhandled exception occurred.");
        }
    }

    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(new GlobalConfiguration());

    private static AppBuilder BuildAvaloniaApp(GlobalConfiguration configuration)
    {
        return AppBuilder.Configure(() => new App(configuration))
            .UsePlatformDetect()
            .With(UiFonts.CreateFontManagerOptions(configuration.ViewConfig.UICulture.Value))
            .LogToTrace();
    }
}
