using System.Runtime;

using Avalonia;
using Avalonia.Media;
using Avalonia.ReactiveUI;

using Beutl.Configuration;
using Beutl.Helpers;
using Beutl.Rendering;
using Beutl.Services;

namespace Beutl;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Restore config
        GlobalConfiguration config = GlobalConfiguration.Instance;
        config.Restore(GlobalConfiguration.DefaultFilePath);
        ViewConfig view = config.ViewConfig;
        CultureInfo.CurrentUICulture = view.UICulture;

        using IDisposable _ = Telemetry.GetDisposable();

        // ProfileOptimizationを有効化
        string jitProfiles = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "jitProfiles");
        if (!Directory.Exists(jitProfiles))
            Directory.CreateDirectory(jitProfiles);

        ProfileOptimization.SetProfileRoot(jitProfiles);
        ProfileOptimization.StartProfile("beutl.jitprofile");

        WaitForExitOtherProcesses();

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
            using (OutProcessDialog.Show(
                Message.OpeningBeutl,
                Message.Changes_to_the_package_are_in_progress,
                Message.To_open_Beutl_close_Beutl_PackageTools,
                icon: "Info",
                progress: true))
            {

                foreach (Process item in processes)
                {
                    if (!item.HasExited)
                    {
                        item.WaitForExit();
                    }
                }
            }
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
            .AfterSetup(_ => Telemetry.CompressLogFiles())
#if DEBUG
            .LogToTrace();
#else
            ;
#endif
    }
}
