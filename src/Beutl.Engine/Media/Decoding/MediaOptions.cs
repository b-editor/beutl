using Beutl.Media.Proxy;

namespace Beutl.Media.Decoding;

/// <summary>Options controlling how a media source is opened.</summary>
public record MediaOptions(
    MediaMode StreamsToLoad = MediaMode.AudioVideo,
    [property: Obsolete("Do not use this property.", true)]
    int SampleRate = 44100)
{
    public bool PreferProxy { get; init; }

    public ProxyPreset PreferredProxyPreset { get; init; } = ProxyPreset.Quarter;
}
