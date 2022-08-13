namespace BeUtl.Media.Decoding;

public sealed record VideoStreamInfo(
    string CodecName,
    Rational Duration,
    PixelSize FrameSize,
    Rational FrameRate)
    : StreamInfo(CodecName, MediaType.Video, Duration)
{
    private Rational? _numFrames;

    public Rational NumFrames
    {
        get
        {
            _numFrames ??= (Duration * FrameRate).Simplify();
            return _numFrames.Value;
        }
        init => _numFrames = value;
    }
}
