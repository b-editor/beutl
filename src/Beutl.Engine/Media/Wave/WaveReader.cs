using System.Diagnostics.CodeAnalysis;

using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Source;

using NAudio.Wave;

namespace Beutl.Media.Wave;

public sealed class WaveReader : MediaReader
{
    private readonly WaveFileReader _reader;
    private readonly ISampleProvider _provider;
    private readonly WaveFormat _waveFormat;

    public WaveReader(string file)
    {
        _reader = new WaveFileReader(file);
        _provider = _reader.ToSampleProvider().ToStereo();
        _waveFormat = _reader.WaveFormat;

        AudioInfo = new AudioStreamInfo(
            CodecName: $"Wave ({_waveFormat.Encoding})",
            Duration: new Rational(_reader.Length, _waveFormat.AverageBytesPerSecond),
            SampleRate: _waveFormat.SampleRate,
            NumChannels: _waveFormat.Channels);
    }

    public override VideoStreamInfo VideoInfo => throw new NotSupportedException();

    public override AudioStreamInfo AudioInfo { get; }

    public override bool HasVideo => false;

    public override bool HasAudio => true;

    protected override bool ReadAudioCore(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound)
    {
        sound = null;
        if (IsDisposed)
            return false;

        _reader.CurrentTime = TimeSpan.FromSeconds(start / (double)_waveFormat.SampleRate);
        sound = SampleProviderReader.ReadStereo(_provider, _waveFormat.SampleRate, length);
        return true;
    }

    protected override bool ReadVideoCore(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
    {
        image = null;
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _reader.Dispose();
    }
}
