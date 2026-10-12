namespace Beutl.Media.Decoding;

public interface IDecoderInfo
{
    string Name { get; }

    MediaReader? Open(string file, MediaOptions options);

    bool IsSupported(string file)
    {
        string extension = Path.GetExtension(file);
        return VideoExtensions().Concat(AudioExtensions())
            .Select(DecoderFileExtensions.Normalize)
            .Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    IEnumerable<string> VideoExtensions();

    IEnumerable<string> AudioExtensions();
}
