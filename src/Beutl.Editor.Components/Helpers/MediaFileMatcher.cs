using Beutl.Media.Decoding;

namespace Beutl.Editor.Components.Helpers;

// How the element adder tells what a file is from its extension, for whatever has to sort files the way it will.
internal static class MediaFileMatcher
{
    public static bool Matches(string file, IEnumerable<string> extensions)
    {
        string ext = Path.GetExtension(file);
        return extensions
            .Select(value =>
            {
                int index = value.LastIndexOf('.');
                return index >= 0 ? value[index..] : value;
            })
            .Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsImage(string file) => Matches(file, SharedFilePickerOptions.ImagePatterns);

    public static IEnumerable<string> VideoExtensions()
        => DecoderRegistry.EnumerateDecoder().SelectMany(decoder => decoder.VideoExtensions());

    public static IEnumerable<string> AudioExtensions()
        => DecoderRegistry.EnumerateDecoder().SelectMany(decoder => decoder.AudioExtensions());
}
