using Avalonia;
using Avalonia.ReactiveUI;

namespace BeUtl;

internal static class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        GC.KeepAlive(typeof(Avalonia.Svg.Skia.SvgImageExtension).Assembly);
        GC.KeepAlive(typeof(Avalonia.Svg.Skia.Svg).Assembly);
        GC.KeepAlive(typeof(FluentIcons.FluentAvalonia.SymbolIcon).Assembly);
        GC.KeepAlive(typeof(FluentIcons.Common.Symbol).Assembly);
        GC.KeepAlive(typeof(AsyncImageLoader.ImageLoader).Assembly);

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseReactiveUI()
            .With(new Win32PlatformOptions()
            {
                UseWindowsUIComposition = true,
                //EnableMultitouch = true,
                CompositionBackdropCornerRadius = 8f
            })
            .LogToTrace();
    }
}
