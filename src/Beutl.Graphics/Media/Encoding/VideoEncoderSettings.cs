namespace Beutl.Media.Encoding;

public sealed record VideoEncoderSettings(
    PixelSize SourceSize,
    PixelSize DestinationSize,
    Rational FrameRate,
    int Bitrate = 5_000_000,
    int KeyframeRate = 12)
    : MediaEncoderSettings;
