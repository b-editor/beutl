using Beutl.Media.Decoding;

namespace Beutl.Media.Wave;

public sealed class WaveDecoderInfo : IDecoderInfo
{
    public string Name => "Wave Reader (unsigned 8bit[PCM], signed 16bit[PCM], signed 24bit[PCM], signed 24bit[PCM], signed 32bit[PCM], 32bit float[IEEE Float])";

    public IEnumerable<string> AudioExtensions()
    {
        yield return ".wav";
        yield return ".wave";
    }

    public MediaReader? Open(string file, MediaOptions options)
    {
        try
        {
            return new WaveReader(file, options);
        }
        catch
        {
            return null;
        }
    }

    public IEnumerable<string> VideoExtensions()
    {
        yield break;
    }
}
