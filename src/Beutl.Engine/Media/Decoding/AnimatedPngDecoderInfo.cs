namespace Beutl.Media.Decoding;

public class AnimatedPngDecoderInfo : IDecoderInfo
{
    public string Name => "Animated PNG Decoder";

    public MediaReader? Open(string file, MediaOptions options)
    {
        try
        {
            return new AnimatedPngReader(file);
        }
        catch
        {
            return null;
        }
    }

    public IEnumerable<string> VideoExtensions()
    {
        yield return ".png";
        yield return ".apng";
    }

    public IEnumerable<string> AudioExtensions()
    {
        yield break;
    }
}
