using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

using Beutl.Threading;

using Vortice.MediaFoundation;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

public static class MFThread
{
    static MFThread()
    {
        Dispatcher = Dispatcher.Spawn(() =>
        {
            Thread.CurrentThread.IsBackground = true;
            Thread.CurrentThread.Name = "Beutl.MediaFoundation";
            // Audio-only opens also create Vortice wrappers while probing the
            // video time origin. Configure SharpGen before any wrapper freezes
            // its process-wide settings, regardless of which stream opens first.
            SharpGen.Runtime.Configuration.EnableObjectTracking = true;
            SharpGen.Runtime.Configuration.EnableReleaseOnFinalizer = true;
            SharpGen.Runtime.Configuration.UseThreadStaticObjectTracking = true;
            MediaFactory.MFStartup();
        });

        if (Application.Current?.ApplicationLifetime is IControlledApplicationLifetime lifetime)
        {
            lifetime.Exit += OnApplicationExit;
        }

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    public static Dispatcher Dispatcher { get; }

    private static void OnApplicationExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        Shutdown();
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Shutdown();
    }

    private static void Shutdown()
    {
        Dispatcher.Invoke(() =>
        {
            MediaFactory.MFShutdown();
        });
        Dispatcher.Shutdown();

        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
    }
}
