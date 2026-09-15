using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using Beutl.Api.Services;
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
            PackageInstaller.RecoverDataPackageInstalls();
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "An unhandled exception occurred.");
        }
    }

    // FontManager must not initialize before Main recovers package payloads.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new FontManagerOptions
            {
                DefaultFamilyName = Media.FontManager.Instance.DefaultTypeface.FontFamily.Name
            })
            .LogToTrace();
    }
}
