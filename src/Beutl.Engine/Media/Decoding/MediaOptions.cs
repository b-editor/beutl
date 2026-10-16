using Beutl.Media.Proxy;

namespace Beutl.Media.Decoding;

/// <summary>Options controlling how a media source is opened.</summary>
public record MediaOptions(
    MediaMode StreamsToLoad = MediaMode.AudioVideo,
    [property: Obsolete("Do not use this property.", true)]
    int SampleRate = 44100)
{
    public bool PreferProxy { get; init; }

    /// <summary>
    /// Whether background work reads the media, such as File Browser thumbnails, proxy generation or AI import,
    /// rather than the editor's preview, playback or timeline thumbnails. A decoder that decodes in another
    /// process decodes background reads in a process of their own, so that a file that crashes it does not
    /// stop the editor's readers.
    /// </summary>
    public bool IsBackground { get; init; }

    public ProxyPreset PreferredProxyPreset { get; init; } = ProxyPreset.Quarter;
}
