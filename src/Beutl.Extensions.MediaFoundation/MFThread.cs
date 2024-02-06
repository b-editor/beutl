using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

using Beutl.Threading;

using SharpDX.MediaFoundation;

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
            MediaManager.Startup();
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
            MediaManager.Shutdown();
        });
        Dispatcher.Shutdown();

        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
    }
}
