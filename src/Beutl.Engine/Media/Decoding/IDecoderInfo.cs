namespace Beutl.Media.Decoding;

public interface IDecoderInfo
{
    string Name { get; }

    /// <summary>
    /// Opens <paramref name="file"/> for the streams that <see cref="MediaOptions.StreamsToLoad"/> requests.
    /// </summary>
    /// <param name="file">The path of the media file.</param>
    /// <param name="options">The streams to load.</param>
    /// <returns>
    /// A reader that follows the rules of <see cref="MediaReader"/> and has each requested stream the file has, or
    /// <see langword="null"/> when this decoder cannot open the file or the file has none of the requested streams.
    /// </returns>
    /// <remarks>
    /// When a decoder cannot open a file, it logs the reason and returns <see langword="null"/> instead of throwing, so
    /// <see cref="DecoderRegistry"/> tries the next decoder.
    /// </remarks>
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
