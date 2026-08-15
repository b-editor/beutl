namespace Beutl.Media.Decoding;

public enum MediaFileKind
{
    None,
    Image,
    Video,
    Audio
}

/// <summary>
/// Single source of truth for "which file extensions can Beutl open".
/// </summary>
/// <remarks>
/// Video and audio extensions are the registered <see cref="IDecoderInfo"/>s' extensions unioned
/// with a baseline, so a side-loaded decoder widens them without any list here being edited while a
/// well-known container still classifies when the decoder that reads it is absent or not yet
/// registered. Still images are decoded by Skia rather than by a decoder, so they keep a fixed list.
/// </remarks>
public static class DecoderFileExtensions
{
    // .png / .apng / .gif / .webp are registered as *video* extensions by the animated-image
    // decoders, so a still-image check has to win over the video check. Classify bakes that
    // ordering in; callers should prefer it over comparing against the sets themselves.
    //
    // Skia's own format table is the authority; the extras are ones it decodes without having an
    // EncodedImageFormat to write them back (.tif/.tiff decode only where the platform supports it,
    // and fall through to no thumbnail where it does not).
    private static readonly HashSet<string> s_image = Graphics.Image.SupportedExtensions
        .Concat([".avif", ".apng", ".tif", ".tiff"])
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    // Classification drives the file browser's icons, tooltips and media search, which must not
    // depend on whether an optional decoder extension has loaded yet — every decoder for these
    // containers ships as a side-loaded extension, and .flac has none at all.
    private static readonly string[] s_videoBaseline =
    [
        ".mp4", ".avi", ".mov", ".mkv", ".wmv", ".flv", ".webm"
    ];

    private static readonly string[] s_audioBaseline =
    [
        ".mp3", ".wav", ".ogg", ".flac", ".aac", ".wma", ".m4a"
    ];

    private static readonly Lock s_lock = new();
    private static HashSet<string>? s_video;
    private static HashSet<string>? s_audio;

    static DecoderFileExtensions()
    {
        DecoderRegistry.DecodersChanged += (_, _) => Invalidate();
    }

    public static IReadOnlyCollection<string> Image => s_image;

    public static IReadOnlyCollection<string> Video =>
        GetOrBuild(ref s_video, i => i.VideoExtensions(), s_videoBaseline);

    public static IReadOnlyCollection<string> Audio =>
        GetOrBuild(ref s_audio, i => i.AudioExtensions(), s_audioBaseline);

    public static MediaFileKind Classify(string file)
    {
        string extension = Path.GetExtension(file);
        if (string.IsNullOrEmpty(extension))
            return MediaFileKind.None;

        if (s_image.Contains(extension))
            return MediaFileKind.Image;
        if (Video.Contains(extension))
            return MediaFileKind.Video;
        if (Audio.Contains(extension))
            return MediaFileKind.Audio;

        return MediaFileKind.None;
    }

    public static bool IsImage(string file) => Classify(file) == MediaFileKind.Image;

    public static bool IsVideo(string file) => Classify(file) == MediaFileKind.Video;

    public static bool IsAudio(string file) => Classify(file) == MediaFileKind.Audio;

    public static bool IsMedia(string file) => Classify(file) != MediaFileKind.None;

    public static string[] GetFilePatterns(Func<IDecoderInfo, IEnumerable<string>> selector)
    {
        return DecoderRegistry.EnumerateDecoder()
            .SelectMany(selector)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(NormalizePattern)
            .ToArray();
    }

    private static HashSet<string> GetOrBuild(
        ref HashSet<string>? cache, Func<IDecoderInfo, IEnumerable<string>> selector, string[] baseline)
    {
        lock (s_lock)
        {
            return cache ??= DecoderRegistry.EnumerateDecoder()
                .SelectMany(selector)
                .Concat(baseline)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void Invalidate()
    {
        lock (s_lock)
        {
            s_video = null;
            s_audio = null;
        }
    }

    private static string NormalizePattern(string extension)
    {
        if (extension.Contains('*', StringComparison.Ordinal))
        {
            return extension;
        }

        return extension.StartsWith('.') ? $"*{extension}" : $"*.{extension}";
    }
}
