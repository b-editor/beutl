namespace Beutl.Media.Decoding;

/// <summary>
/// Options controlling how a media source is opened. Additively extensible (feature 003): a future
/// proxy / optimized-media workflow can add an optional decode-scale hint here without touching
/// existing call sites or the GPL FFmpeg-worker IPC protocol. The decoded pixel size then becomes the
/// source operation's <c>EffectiveScale</c> (distinct from its logical footprint), so layout is
/// unaffected. Feature 003 ships only that seam, not reduced-decode.
/// </summary>
public record MediaOptions(
    MediaMode StreamsToLoad = MediaMode.AudioVideo,
    [property: Obsolete("Do not use this property.", true)]
    int SampleRate = 44100);
