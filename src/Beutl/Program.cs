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
