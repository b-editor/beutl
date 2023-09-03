using Beutl.Media.Wave;

namespace Beutl.Media.Decoding;

public static class DecoderRegistry
{
    private static readonly List<IDecoderInfo> s_registered = new()
    {
        new WaveDecoderInfo()
    };

    public static IEnumerable<IDecoderInfo> EnumerateDecoder()
    {
        return s_registered;
    }

    public static MediaReader? OpenMediaFile(string file, MediaOptions options)
    {
        foreach (IDecoderInfo decoder in GuessDecoder(file))
        {
            if (decoder.Open(file, options) is { } reader)
            {
                return reader;
            }
        }

        return null;
    }

    public static IDecoderInfo[] GuessDecoder(string file)
    {
        return s_registered.Where(i => i.IsSupported(file)).ToArray();
    }

    public static void Register(IDecoderInfo decoder)
    {
        s_registered.Add(decoder);
    }
}
