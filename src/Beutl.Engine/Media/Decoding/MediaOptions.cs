namespace Beutl.Media.Decoding;

/// <summary>Options controlling how a media source is opened.</summary>
public record MediaOptions(
    MediaMode StreamsToLoad = MediaMode.AudioVideo,
    [property: Obsolete("Do not use this property.", true)]
    int SampleRate = 44100);
