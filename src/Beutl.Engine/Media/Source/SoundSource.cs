using System.Diagnostics.CodeAnalysis;

using Beutl.Media.Decoding;
using Beutl.Media.Music;

namespace Beutl.Media.Source;

public class SoundSource(Ref<MediaReader> mediaReader, string fileName) : ISoundSource
{
    ~SoundSource()
    {
        Dispose();
    }

    public TimeSpan Duration { get; } = TimeSpan.FromSeconds(mediaReader.Value.AudioInfo.Duration.ToDouble());

    public int SampleRate { get; } = mediaReader.Value.AudioInfo.SampleRate;

    public int NumChannels { get; } = mediaReader.Value.AudioInfo.NumChannels;

    public bool IsDisposed { get; private set; }

    public string Name { get; } = fileName;

    public static SoundSource Open(string fileName)
    {
        var reader = MediaReader.Open(fileName, new(MediaMode.Audio));
        return new SoundSource(Ref<MediaReader>.Create(reader), fileName);
    }

    public static bool TryOpen(string fileName, out SoundSource? result)
    {
        try
        {
            result = Open(fileName);
            return true;
        }
        catch
        {
            result = null;
            return false;
        }
    }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            mediaReader.Dispose();
            GC.SuppressFinalize(this);
            IsDisposed = true;
        }
    }

    public SoundSource Clone()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        return new SoundSource(mediaReader.Clone(), Name);
    }

    public bool Read(int start, int length, [NotNullWhen(true)] out IPcm? sound)
    {
        if (IsDisposed)
        {
            sound = null;
            return false;
        }

        return mediaReader.Value.ReadAudio(start, length, out sound);
    }

    public bool Read(TimeSpan start, TimeSpan length, [NotNullWhen(true)] out IPcm? sound)
    {
        if (IsDisposed)
        {
            sound = null;
            return false;
        }

        return mediaReader.Value.ReadAudio(ToSamples(start), ToSamples(length), out sound);
    }

    public bool Read(TimeSpan start, int length, [NotNullWhen(true)] out IPcm? sound)
    {
        if (IsDisposed)
        {
            sound = null;
            return false;
        }

        return mediaReader.Value.ReadAudio(ToSamples(start), length, out sound);
    }

    public bool Read(int start, TimeSpan length, [NotNullWhen(true)] out IPcm? sound)
    {
        if (IsDisposed)
        {
            sound = null;
            return false;
        }

        return mediaReader.Value.ReadAudio(start, ToSamples(length), out sound);
    }

    ISoundSource ISoundSource.Clone() => Clone();

    private int ToSamples(TimeSpan timeSpan)
    {
        return (int)(timeSpan.TotalSeconds * SampleRate);
    }
}
