namespace Beutl.Media.Decoding;

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

        if (Rational.IsInfinity(frameRate) || Rational.IsNaN(frameRate))
            throw new ArgumentException($"{nameof(frameRate)} cannot be specified as Infinity or NaN.", nameof(frameRate));
        
        if (Rational.IsInfinity(duration) || Rational.IsNaN(duration))
            throw new ArgumentException($"{nameof(duration)} cannot be specified as Infinity or NaN.", nameof(duration));

        NumFrames = (long)(duration * frameRate).ToDouble();
    }

    public VideoStreamInfo(
        string codecName,
        long numFrames,
        PixelSize frameSize,
        Rational frameRate)
        : base(
            codecName,
            MediaType.Video,
            Rational.IsNormal(frameRate)
                ? new Rational(numFrames) / frameRate
                : Rational.Zero)
    {
        FrameSize = frameSize;
        FrameRate = frameRate;

        NumFrames = numFrames;
    }

    public PixelSize FrameSize { get; init; }

    public Rational FrameRate { get; init; }

    public long NumFrames { get; init; }
}
