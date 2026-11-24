using System.Runtime;
using Avalonia;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Controls.Styling;
using Beutl.Editor.VersionControl;
using Beutl.Graphics.Rendering;
using Beutl.Helpers;
using Beutl.Services;
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

        var config = new GlobalConfiguration();
        config.Restore(GlobalConfiguration.DefaultFilePath);
        ViewConfig view = config.ViewConfig;
        CultureInfo.CurrentUICulture = view.UICulture.Value;

        using var telemetry = new Telemetry(config.TelemetryConfig);
        config.LogRestoreFailures();

        string jitProfiles = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "jitProfiles");
        Directory.CreateDirectory(jitProfiles);

        ProfileOptimization.SetProfileRoot(jitProfiles);
        ProfileOptimization.StartProfile("beutl.jitprofile");

        UnhandledExceptionHandler.Initialize(telemetry._sessionId, config);

        PackageInstaller.RecoverDataPackageInstalls();
        BuildAvaloniaApp(config, telemetry._sessionId)
            .StartWithClassicDesktopLifetime(args);

        UnhandledExceptionHandler.Exit(config);
    }

    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(new GlobalConfiguration(), string.Empty);

    private static AppBuilder BuildAvaloniaApp(GlobalConfiguration configuration, string sessionId)
    {
        return AppBuilder.Configure(() => new App(configuration, sessionId))
            .UsePlatformDetect()
            .UseReactiveUI(builder => builder.WithMainThreadScheduler(UiThreadScheduler.Instance))
            .With(new Win32PlatformOptions()
            {
                WinUICompositionBackdropCornerRadius = 8f,
                UseCompositionSwapChain = true
            })
            .With(new X11PlatformOptions()
            {
                UseXShmFramebuffer = true
            })
            .With(UiFonts.CreateFontManagerOptions(configuration.ViewConfig.UICulture.Value))
            .AfterSetup(_ => Telemetry.CompressLogFiles())
#if DEBUG
            .LogToTrace();
#else
            ;
#endif
    }
}
