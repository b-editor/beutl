using Beutl.Media.Proxy;

namespace Beutl.Media.Decoding;

/// <summary>Options controlling how a media source is opened.</summary>
public record MediaOptions(
    MediaMode StreamsToLoad = MediaMode.AudioVideo,
    [property: Obsolete("Do not use this property.", true)]
    int SampleRate = 44100)
{
    public bool PreferProxy { get; init; }

    /// <summary>Whether background work such as File Browser thumbnails, proxies or AI import reads the media, not the editor.</summary>
    public bool IsBackground { get; init; }

    public ProxyPreset PreferredProxyPreset { get; init; } = ProxyPreset.Quarter;
}
