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
        try
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
        catch
        {
            // The base finalizer would otherwise dispose a reader that never opened and crash
            // the process from the finalizer thread; WaveDecoderInfo swallows this exception.
            _reader?.Dispose();
            GC.SuppressFinalize(this);
            throw;
        }
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

        // Seek by bytes; CurrentTime rounds through a TimeSpan and lands many offsets a frame early.
        _reader.Position = (long)start * _waveFormat.BlockAlign;
        sound = SampleProviderReader.ReadStereo(_provider, _waveFormat.SampleRate, length);
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
