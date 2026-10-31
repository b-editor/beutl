using Beutl.Audio;

namespace Beutl.Media.Decoding;

/// <param name="NumChannels">
/// The source stream's native channel count, exposed as metadata. This can differ from
/// <see cref="ChannelLayout"/>, which describes the buffers returned by <see cref="MediaReader.ReadAudio"/>.
/// Do not use this value to size decode buffers.
/// </param>
public sealed record AudioStreamInfo(
    string CodecName,
    Rational Duration,
    int SampleRate,
    int NumChannels)
    : StreamInfo(CodecName, MediaType.Audio, Duration)
{
    private Rational? _numSamples;
    private AudioChannelLayout _channelLayout = AudioChannelLayout.Stereo;

    /// <summary>
    /// The channel count and order returned by <see cref="MediaReader.ReadAudio"/>. Defaults to stereo
    /// for decoders that convert their native channels before returning a buffer.
    /// </summary>
    public AudioChannelLayout ChannelLayout
    {
        get => _channelLayout;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value.ChannelCount, nameof(value));
            _channelLayout = value;
        }
    }

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
