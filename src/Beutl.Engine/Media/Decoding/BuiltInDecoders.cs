using Beutl.Media.Wave;

namespace Beutl.Media.Decoding;

/// <summary>
/// The decoders Beutl.Engine provides itself, which need no native runtime: WAV, animated GIF and WebP,
/// and APNG. Every host registers these same instances: the app through its built-in decoding
/// extensions, and the headless MCP server through <see cref="Register"/>.
/// </summary>
public static class BuiltInDecoders
{
    public static WaveDecoderInfo Wave { get; } = new();

    public static AnimatedImageDecoderInfo AnimatedImage { get; } = new();

    public static AnimatedPngDecoderInfo AnimatedPng { get; } = new();

    public static IReadOnlyList<IDecoderInfo> All { get; } = [Wave, AnimatedImage, AnimatedPng];

    /// <summary>Registers each built-in decoder that is not registered yet.</summary>
    public static void Register(DecoderRegistry decoders)
    {
        lock (decoders.RegistrationLock)
        {
            IDecoderInfo[] registered = decoders.EnumerateDecoder().ToArray();
            foreach (IDecoderInfo decoder in All)
            {
                if (!registered.Contains(decoder))
                {
                    decoders.Register(decoder);
                }
            }
        }
    }
}
