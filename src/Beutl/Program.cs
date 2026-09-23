using System.Runtime;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Graphics.Rendering;
using Beutl.Helpers;
using Beutl.Services;
using ReactiveUI.Avalonia;

namespace Beutl;

internal static class Program
{
    internal static PackageLinkBroker? ActivationBroker { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
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

    // Keep FontManager's beforefieldinit initialization inside the builder call,
    // after Main has recovered package fonts.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseReactiveUI(_ => { })
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
