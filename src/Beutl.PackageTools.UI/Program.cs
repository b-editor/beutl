using Avalonia;

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

        using IDisposable _ = Telemetry.GetDisposable(GetSessionId());

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
