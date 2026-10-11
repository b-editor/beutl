using System.Runtime;
using Avalonia;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Controls.Styling;
using Beutl.Editor.VersionControl;
using Beutl.Graphics.Rendering;
using Beutl.Helpers;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using ReactiveUI.Avalonia.Reactive;

namespace Beutl;

internal static class Program
{
    internal static PackageLinkBroker? ActivationBroker { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        // Git LFS starts this executable as Beutl's hosted Git upload agent; no UI is involved.
        if (args is [HostedGitLfsTransferAgent.CommandLineFlag])
        {
            Environment.ExitCode = HostedGitLfsTransferAgent.RunAsync().GetAwaiter().GetResult();
            return;
        }

        PackageLinkLaunchResult launch = OperatingSystem.IsMacOS()
            ? new(PackageLinkLaunchAction.StartApplication, null, args)
            : PackageLinkLauncher.PrepareAsync(args).GetAwaiter().GetResult();
        using PackageLinkBroker? broker = launch.Broker;
        if (launch.Action != PackageLinkLaunchAction.StartApplication)
        {
            if (launch.Action == PackageLinkLaunchAction.Failed)
            {
                Console.Error.WriteLine("Could not send the installation link to Beutl. Please try again.");
                Environment.ExitCode = 1;
            }
            return;
        }
        args = launch.Arguments;
        ActivationBroker = broker;

        // Restore config
        GlobalConfiguration config = GlobalConfiguration.Instance;
        config.Restore(GlobalConfiguration.DefaultFilePath);
        ViewConfig view = config.ViewConfig;
        CultureInfo.CurrentUICulture = view.UICulture;

        using IDisposable _ = Telemetry.GetDisposable();
        LogRestoreFailures(config);

        // ProfileOptimizationを有効化
        string jitProfiles = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "jitProfiles");
        if (!Directory.Exists(jitProfiles))
            Directory.CreateDirectory(jitProfiles);

        ProfileOptimization.SetProfileRoot(jitProfiles);
        ProfileOptimization.StartProfile("beutl.jitprofile");

        UnhandledExceptionHandler.Initialize();

        PackageInstaller.RecoverDataPackageInstalls();
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

        // 正常に終了した
        UnhandledExceptionHandler.Exit();
    }

    // Restore runs before logging is set up, so what it could not read is reported here.
    private static void LogRestoreFailures(GlobalConfiguration config)
    {
        if (config.RestoreFailures.Count == 0)
            return;

        ILogger logger = Log.CreateLogger(typeof(Program));
        foreach (ConfigurationRestoreFailure failure in config.RestoreFailures)
        {
            logger.LogWarning(
                failure.Exception,
                "Could not read {Setting} from settings.json, so it keeps its default. The file as read is kept as settings.json.bak.",
                failure.Setting);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseReactiveUI(builder => builder.WithMainThreadScheduler(UiThreadScheduler.Instance))
            .With(new Win32PlatformOptions()
            {
                WinUICompositionBackdropCornerRadius = 8f
            })
            .With(UiFonts.CreateFontManagerOptions(GlobalConfiguration.Instance.ViewConfig.UICulture))
            .AfterSetup(_ => Telemetry.CompressLogFiles())
#if DEBUG
            .LogToTrace();
#else
            ;
#endif
    }
}
