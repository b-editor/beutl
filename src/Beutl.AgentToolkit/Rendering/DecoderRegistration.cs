using Beutl.Extensions.AVFoundation.Decoding;
using Beutl.Extensions.FFmpeg.Decoding;
using Beutl.Media.Decoding;

namespace Beutl.AgentToolkit.Rendering;

/// <summary>
/// Registers the media decoders of the headless MCP server, which loads no extensions: the engine's
/// built-in decoders (WAV, animated GIF and WebP, APNG), FFmpeg, and AVFoundation on macOS, in the
/// order the app registers them at startup.
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
            if (!DecoderRegistry.EnumerateDecoder().OfType<FFmpegDecoderInfo>().Any())
            {
                DecoderRegistry.Register(new FFmpegDecoderInfo(new FFmpegDecodingSettings()));
            }

            if (OperatingSystem.IsMacOS() && !DecoderRegistry.EnumerateDecoder().OfType<AVFDecoderInfo>().Any())
            {
                new AVFDecodingExtension().Load();
            }

            s_registered = true;
        }
    }
}
