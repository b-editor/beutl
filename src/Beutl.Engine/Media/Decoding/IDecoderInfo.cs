namespace Beutl.Media.Decoding;

public interface IDecoderInfo
{
    string Name { get; }

    MediaReader? Open(string file, MediaOptions options);

    // Nothing constrains the shape a decoder returns, and DecoderFileExtensions accepts "mp4",
    // ".mp4" and "*.mp4" alike. Selection has to read the claims the same way, or the file browser
    // offers a format whose decoder GuessDecoder never matches.
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
