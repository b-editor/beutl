namespace Beutl.Media.Encoding;

public sealed record AudioEncoderSettings(
    int SampleRate = 44_100,
    int Channels = 2,
    int Bitrate = 128_000)
    : MediaEncoderSettings;
