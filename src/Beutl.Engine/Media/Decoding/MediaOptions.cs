namespace Beutl.Media.Decoding;

/// <summary>
/// Options controlling how a media source is opened. Intentionally additively extensible
/// (feature 003): a future proxy / optimized-media workflow adds an optional decode-scale hint here
/// without changing existing call sites or the GPL FFmpeg-worker IPC protocol. When that lands, the
/// decoded pixel size becomes the source operation's <c>EffectiveScale</c> (distinct from its logical
/// footprint), so layout is unaffected. Feature 003 ships only that layout seam, not reduced-decode.
/// </summary>
public record MediaOptions(
    MediaMode StreamsToLoad = MediaMode.AudioVideo,
    [property: Obsolete("Do not use this property.", true)]
    int SampleRate = 44100);
