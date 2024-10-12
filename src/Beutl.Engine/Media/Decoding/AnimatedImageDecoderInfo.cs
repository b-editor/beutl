namespace Beutl.Media.Decoding;

public class AnimatedImageDecoderInfo : IDecoderInfo
{
    public string Name => "Animated Image Decoder";

    public MediaReader? Open(string file, MediaOptions options)
    {
        try
        {
            return new AnimatedImageReader(file);
        }
        catch
        {
            return null;
        }
    }

    public IEnumerable<string> VideoExtensions()
    {
        yield return ".gif";
        yield return ".webp";
    }

    public IEnumerable<string> AudioExtensions()
    {
        yield break;
    }
}
