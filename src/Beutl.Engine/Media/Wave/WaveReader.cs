using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

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

    public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out IPcm? sound)
    {
        sound = null;
        if (IsDisposed)
            return false;

        _reader.CurrentTime = TimeSpan.FromSeconds(start / (double)_waveFormat.SampleRate);

        var tmp = new Pcm<Stereo32BitFloat>(_waveFormat.SampleRate, (int)(length / (double)_waveFormat.SampleRate * _waveFormat.SampleRate));

        float[] buffer = new float[tmp.NumSamples * 2];
        int count = _provider.Read(buffer, 0, tmp.NumSamples * 2);
        if (count >= 0)
        {
            buffer.CopyTo(MemoryMarshal.Cast<Stereo32BitFloat, float>(tmp.DataSpan));

            sound = tmp;
            return true;
        }
        else
        {
            return false;
        }
    }

    public override bool ReadVideo(int frame, [NotNullWhen(true)] out IBitmap? image)
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
