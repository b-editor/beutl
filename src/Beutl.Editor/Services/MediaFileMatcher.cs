using Beutl.Media.Decoding;

namespace Beutl.Editor.Services;

// How the element adder tells what a file is from its extension, for whatever has to sort files the way it will.
internal static class MediaFileMatcher
{
    // SKEncodedImageFormat; shared by the image picker and file imports.
    internal static readonly string[] ImagePatterns =
    [
        "*.bmp", "*.gif", "*.ico", "*.jpg", "*.jpeg", "*.png", "*.svg", "*.wbmp", "*.webp",
        "*.pkm", "*.ktx", "*.astc", "*.dng", "*.heif", "*.avif"
    ];

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

    public static bool IsImage(string file) => Matches(file, ImagePatterns);

    public static IEnumerable<string> VideoExtensions(DecoderRegistry decoders)
        => decoders.FileExtensions.RegisteredVideo;

    public static IEnumerable<string> AudioExtensions(DecoderRegistry decoders)
        => decoders.FileExtensions.RegisteredAudio;
}
