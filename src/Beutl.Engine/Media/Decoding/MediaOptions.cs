namespace Beutl.Media.Decoding;

public record MediaOptions(
    MediaMode StreamsToLoad = MediaMode.AudioVideo,
    [property: Obsolete("Do not use this property.", true)]
    int SampleRate = 44100);
