namespace BeUtl.Media.Decoding;

public sealed record VideoStreamInfo : StreamInfo
{
    public VideoStreamInfo(
        string codecName,
        Rational duration,
        PixelSize frameSize,
        Rational frameRate)
        : base(codecName, MediaType.Video, duration)
    {
        FrameSize = frameSize;
        FrameRate = frameRate;

        NumFrames = (long)(duration * frameRate).ToDouble();
    }
    
    public VideoStreamInfo(
        string codecName,
        long numFrames,
        PixelSize frameSize,
        Rational frameRate)
        : base(codecName, MediaType.Video, new Rational(numFrames) / frameRate)
    {
        FrameSize = frameSize;
        FrameRate = frameRate;

        NumFrames = numFrames;
    }

    public PixelSize FrameSize { get; init; }

    public Rational FrameRate { get; init; }

    public long NumFrames { get; init; }
}
