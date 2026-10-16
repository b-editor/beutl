namespace Beutl.Media.Decoding;

/// <param name="NumChannels">
/// The source stream's native channel count, exposed as metadata (display, thumbnails). This is
/// independent of the decode output format: <see cref="MediaReader.ReadAudio"/> always yields a stereo
/// <see cref="Beutl.Audio.AudioBuffer"/>, so this value and <see cref="Beutl.Audio.AudioBuffer.ChannelCount"/>
/// intentionally differ for non-stereo sources.
/// Do not use it to size decode buffers.
/// </param>
public sealed record AudioStreamInfo(
    string CodecName,
    Rational Duration,
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
