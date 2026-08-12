using Avalonia;
using Avalonia.Media;
using Beutl.Configuration;
using Beutl.Logging;
using Beutl.Services;

namespace Beutl.PackageTools.UI;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Restore config
        GlobalConfiguration config = GlobalConfiguration.Instance;
        config.Restore(GlobalConfiguration.DefaultFilePath);

        using IDisposable _ = Telemetry.GetDisposable(
            GetTelemetrySessionId(config.TelemetryConfig),
            TelemetryHostKind.PackageTools);
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
            .With(new FontManagerOptions
            {
                DefaultFamilyName = Media.FontManager.Instance.DefaultTypeface.FontFamily.Name
            })
            .LogToTrace();

    internal static string? GetTelemetrySessionId(TelemetryConfig configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.UsageAnalytics == true
            ? Telemetry.GetSessionIdFromEnvironment()
            : null;
    }
}
