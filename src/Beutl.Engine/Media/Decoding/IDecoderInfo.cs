namespace Beutl.Media.Decoding;

public interface IDecoderInfo
{
    string Name { get; }

    /// <summary>Opens <paramref name="file"/> as a <see cref="MediaReader"/> for the requested streams.</summary>
    /// <returns>
    /// <see langword="null"/>, without throwing, when it cannot open the file or the file has none of the streams.
    /// </returns>
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
