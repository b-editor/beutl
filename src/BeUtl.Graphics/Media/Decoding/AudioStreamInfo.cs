namespace BeUtl.Media.Decoding;

public sealed record AudioStreamInfo(
    string CodecName,
    Rational Duration,
    PixelSize FrameSize,
    int SampleRate,
    int NumChannels)
    : StreamInfo(CodecName, MediaType.Audio, Duration)
{
    private Rational? _numSamples;

    public Rational NumSamples
    {
        get
        {
            _numSamples ??= Duration * new Rational(SampleRate);
            return _numSamples.Value;
        }
        init => _numSamples = value;
    }
}
