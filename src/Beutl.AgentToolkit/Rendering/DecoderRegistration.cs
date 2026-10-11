using Beutl.Extensions.AVFoundation.Decoding;
using Beutl.Media.Decoding;

namespace Beutl.AgentToolkit.Rendering;

/// <summary>
/// Registers the media decoders of the headless MCP server, which loads no extensions: the engine's
/// built-in decoders (WAV, animated GIF and WebP, APNG) and AVFoundation on macOS, as the app does at
/// startup. FFmpeg's decoder is not among them yet: it still lives in Beutl.Extensions.FFmpeg, next to
/// the app's install dialog.
/// </summary>
public static class DecoderRegistration
{
    private static readonly object s_lock = new();
    private static bool s_registered;

    // DecoderRegistry is process-wide, so this registers once however many servers a process builds.
    public static void EnsureRegistered()
    {
        lock (s_lock)
        {
            if (s_registered)
            {
                return;
            }

            BuiltInDecoders.Register();
            // A process that also loaded the app's extensions (such as a test host) has its own.
            if (OperatingSystem.IsMacOS() && !DecoderRegistry.EnumerateDecoder().OfType<AVFDecoderInfo>().Any())
            {
                new AVFDecodingExtension().Load();
            }

            s_registered = true;
        }
    }
}
