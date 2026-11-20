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
    public static void EnsureRegistered(DecoderRegistry decoders)
    {
        lock (decoders.RegistrationLock)
        {
            BuiltInDecoders.Register(decoders);
            if (!decoders.EnumerateDecoder().OfType<FFmpegDecoderInfo>().Any())
            {
                decoders.Register(new FFmpegDecoderInfo(new FFmpegDecodingSettings()));
            }

            if (OperatingSystem.IsMacOS() && !decoders.EnumerateDecoder().OfType<AVFDecoderInfo>().Any())
            {
                decoders.Register(new AVFDecodingExtension().GetDecoderInfo());
            }
        }
    }
}
