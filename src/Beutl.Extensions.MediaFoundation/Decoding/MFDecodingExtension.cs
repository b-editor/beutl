using Beutl.Extensibility;
using Beutl.Media.Decoding;

using SharpDX.MediaFoundation;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

[Export]
public sealed class MFDecodingExtension : DecodingExtension
{
    public override string Name => "MediaFoundationDecoding";

    public override string DisplayName => "Media Foundation Decoding";

    public override IDecoderInfo GetDecoderInfo()
    {
        return new MFDecoderInfo();
    }

    public override void Load()
    {
        base.Load();

        MFThread.Dispatcher.Invoke(() =>
        {
            Thread.CurrentThread.IsBackground = true;
            MediaManager.Startup();
        });

        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Shutdown();
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        Shutdown();
    }

    private void Shutdown()
    {
        MFThread.Dispatcher.Invoke(() =>
        {
            MediaManager.Shutdown();
        });
        MFThread.Dispatcher.Stop();

        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
    }
}
