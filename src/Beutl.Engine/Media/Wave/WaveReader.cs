using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
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

    public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound)
    {
        sound = null;
        if (IsDisposed)
            return false;

        if (length <= 0)
        {
            sound = Ref<IPcm>.Create(new Pcm<Stereo32BitFloat>(_waveFormat.SampleRate, 0));
            return true;
        }

        _reader.CurrentTime = TimeSpan.FromSeconds(start / (double)_waveFormat.SampleRate);

        // ToStereo() gives 2 floats per frame, so the provider's element count maps to frames via /2.
        float[] buffer = new float[length * 2];
        int frames = _provider.Read(buffer, 0, buffer.Length) / 2;
        if (frames <= 0)
        {
            return false;
        }

        var pcm = new Pcm<Stereo32BitFloat>(_waveFormat.SampleRate, frames);
        buffer.AsSpan(0, frames * 2).CopyTo(MemoryMarshal.Cast<Stereo32BitFloat, float>(pcm.DataSpan));

        sound = Ref<IPcm>.Create(pcm);
        return true;
    }

    public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
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
