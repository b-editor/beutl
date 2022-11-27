using System.Diagnostics.CodeAnalysis;

using Beutl.Media.Decoding;
using Beutl.Media.Music;

namespace Beutl.Media.Source;

public class SoundSource : ISoundSource
{
    private readonly MediaSourceManager.Ref<MediaReader> _mediaReader;

    public SoundSource(MediaSourceManager.Ref<MediaReader> mediaReader, string fileName)
    {
        _mediaReader = mediaReader;
        Name = fileName;
        Duration = TimeSpan.FromSeconds(mediaReader.Value.AudioInfo.Duration.ToDouble());
        SampleRate = mediaReader.Value.AudioInfo.SampleRate;
        NumChannels = mediaReader.Value.AudioInfo.NumChannels;
    }

    ~SoundSource()
    {
        Dispose();
    }

    public TimeSpan Duration { get; }

    public int SampleRate { get; }

    public int NumChannels { get; }

    public bool IsDisposed { get; private set; }

    public string Name { get; }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            _mediaReader.Dispose();
            GC.SuppressFinalize(this);
            IsDisposed = true;
        }
    }

    public bool Read(int start, int length, [NotNullWhen(true)] out IPcm? sound)
    {
        if (IsDisposed)
        {
            sound = null;
            return false;
        }

        return _mediaReader.Value.ReadAudio(start, length, out sound);
    }

    public bool Read(TimeSpan start, TimeSpan length, [NotNullWhen(true)] out IPcm? sound)
    {
        if (IsDisposed)
        {
            sound = null;
            return false;
        }

        return _mediaReader.Value.ReadAudio(ToSamples(start), ToSamples(length), out sound);
    }

    public bool Read(TimeSpan start, int length, [NotNullWhen(true)] out IPcm? sound)
    {
        if (IsDisposed)
        {
            sound = null;
            return false;
        }

        return _mediaReader.Value.ReadAudio(ToSamples(start), length, out sound);
    }

    public bool Read(int start, TimeSpan length, [NotNullWhen(true)] out IPcm? sound)
    {
        if (IsDisposed)
        {
            sound = null;
            return false;
        }

        return _mediaReader.Value.ReadAudio(start, ToSamples(length), out sound);
    }

    private int ToSamples(TimeSpan timeSpan)
    {
        return (int)(timeSpan.TotalSeconds * SampleRate);
    }
}
