using Avalonia;

using Beutl.Configuration;
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
        GlobalConfiguration config = GlobalConfiguration.Instance;
        config.Restore(GlobalConfiguration.DefaultFilePath);

        using IDisposable _ = Telemetry.GetDisposable(GetSessionId());
        ILogger<Program> logger = Log.CreateLogger<Program>();

        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "An unhandled exception occurred.");
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
