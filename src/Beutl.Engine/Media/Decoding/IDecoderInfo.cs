namespace Beutl.Media.Decoding;

public interface IDecoderInfo
{
    string Name { get; }

    MediaReader? Open(string file, MediaOptions options);

    bool IsSupported(string file)
    {
        return VideoExtensions().Concat(AudioExtensions()).Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);
    }

    IEnumerable<string> VideoExtensions();

    IEnumerable<string> AudioExtensions();
}
