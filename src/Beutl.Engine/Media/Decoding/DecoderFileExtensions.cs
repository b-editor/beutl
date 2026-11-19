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
/// with a baseline. Still images are decoded by Skia rather than by a decoder, so they keep a fixed list.
/// </remarks>
public sealed class DecoderFileExtensions
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

    // A decoder's extension claim is not always exclusive: FFmpeg reads '.ts' as an MPEG transport
    // stream, and it is TypeScript everywhere else. Handing a source file to a video decoder for a
    // thumbnail, media probe and the recursive media search is the worse of the two readings, so
    // these lose to the non-media meaning even while a decoder declares them.
    private static readonly HashSet<string> s_nonMediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ts"
    };

    private sealed record Extensions(string[] Registered, HashSet<string> Classified);

    private readonly Lock _lock = new();
    private readonly DecoderRegistry _registry;
    private Extensions? _video;
    private Extensions? _audio;

    internal DecoderFileExtensions(DecoderRegistry registry)
    {
        _registry = registry;
        registry.DecodersChanged += (_, _) => Invalidate();
    }

    public static IReadOnlyCollection<string> Image => s_image;

    private Extensions VideoExtensions => GetOrBuild(ref _video, i => i.VideoExtensions(), s_videoBaseline);

    private Extensions AudioExtensions => GetOrBuild(ref _audio, i => i.AudioExtensions(), s_audioBaseline);

    internal IReadOnlyList<string> RegisteredVideo => VideoExtensions.Registered;

    internal IReadOnlyList<string> RegisteredAudio => AudioExtensions.Registered;

    public IReadOnlyCollection<string> Video => VideoExtensions.Classified;

    public IReadOnlyCollection<string> Audio => AudioExtensions.Classified;

    public MediaFileKind Classify(string file)
    {
        string extension = Path.GetExtension(file);
        if (string.IsNullOrEmpty(extension) || s_nonMediaExtensions.Contains(extension))
            return MediaFileKind.None;

        if (s_image.Contains(extension))
            return MediaFileKind.Image;
        if (Video.Contains(extension))
            return MediaFileKind.Video;
        if (Audio.Contains(extension))
            return MediaFileKind.Audio;

        return MediaFileKind.None;
    }

    public bool IsImage(string file) => Classify(file) == MediaFileKind.Image;

    public bool IsVideo(string file) => Classify(file) == MediaFileKind.Video;

    public bool IsAudio(string file) => Classify(file) == MediaFileKind.Audio;

    public bool IsMedia(string file) => Classify(file) != MediaFileKind.None;

    public string[] GetFilePatterns(Func<IDecoderInfo, IEnumerable<string>> selector)
    {
        return _registry.EnumerateDecoder()
            .SelectMany(selector)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(NormalizePattern)
            .ToArray();
    }

    private Extensions GetOrBuild(ref Extensions? cache, Func<IDecoderInfo, IEnumerable<string>> selector, string[] baseline)
    {
        lock (_lock)
        {
            if (cache != null) return cache;
            string[] registered = _registry.EnumerateDecoder().SelectMany(selector).ToArray();
            var classified = registered.Concat(baseline).Select(Normalize)
                .Where(x => x.Length > 1).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return cache = new Extensions(registered, classified);
        }
    }

    /// <summary>
    /// Rewrites a decoder's extension claim to the leading-dot form.
    /// </summary>
    /// <remarks>
    /// <see cref="IDecoderInfo"/> does not enforce a shape, so <c>mp4</c>, <c>.mp4</c> and
    /// <c>*.mp4</c> all reach here. Classification, decoder selection and the file picker each read
    /// the claims through this, or one of them accepts a format the others do not.
    /// </remarks>
    public static string Normalize(string extension)
    {
        string trimmed = extension.TrimStart('*');
        return trimmed.StartsWith('.') ? trimmed : $".{trimmed}";
    }

    private void Invalidate()
    {
        lock (_lock)
        {
            _video = null;
            _audio = null;
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
