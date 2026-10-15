namespace Beutl.Media.Decoding;

/// <param name="NumChannels">
/// The source stream's native channel count, exposed as metadata (display, thumbnails). This is
/// independent of the decode output format: <see cref="MediaReader.ReadAudio"/> always yields
/// <c>Pcm&lt;Stereo32BitFloat&gt;</c> (<c>NumChannels == 2</c>), so this value and
/// <see cref="Beutl.Media.Music.IPcm.NumChannels"/> intentionally differ for non-stereo sources.
/// Do not use it to size decode buffers.
/// </param>
public sealed record AudioStreamInfo(
    string CodecName,
    Rational Duration,
    int SampleRate,
    int NumChannels)
    : StreamInfo(CodecName, MediaType.Audio, Duration)
{
    public Rational NumSamples => Duration * new Rational(SampleRate);
}
