namespace Beutl.Media.Decoding;

public record MediaOptions(
    MediaMode StreamsToLoad = MediaMode.AudioVideo,
    int SampleRate = 44100);
