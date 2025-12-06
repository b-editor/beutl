using System.Diagnostics.CodeAnalysis;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Serialization;

namespace Beutl.Media.Source;

public class SoundSource : ISoundSource
{
    private Ref<MediaReader>? _mediaReader;
    private Uri? _uri;

    public SoundSource()
    {
    }

    ~SoundSource()
    {
        Dispose();
    }

    public TimeSpan Duration =>
        _mediaReader?.Value.AudioInfo.Duration != null
            ? TimeSpan.FromSeconds(_mediaReader.Value.AudioInfo.Duration.ToDouble())
            : throw new InvalidOperationException("MediaReader is not set.");

    public int SampleRate => _mediaReader?.Value.AudioInfo.SampleRate ??
                             throw new InvalidOperationException("MediaReader is not set.");

    public int NumChannels => _mediaReader?.Value.AudioInfo.NumChannels ??
                              throw new InvalidOperationException("MediaReader is not set.");

    public bool IsDisposed { get; private set; }

    public Uri Uri => _uri ?? throw new InvalidOperationException("URI is not set.");

    public static SoundSource Open(string fileName)
    {
        var reader = MediaReader.Open(fileName, new(MediaMode.Audio));
        return new SoundSource
        {
            _mediaReader = Ref<MediaReader>.Create(reader),
            _uri = UriHelper.CreateFromPath(fileName)
        };
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

    public static SoundSource Open(Uri uri)
    {
        var source = new SoundSource();
        source.ReadFrom(uri);
        return source;
    }

    public static bool TryOpen(Uri uri, out SoundSource? result)
    {
        try
        {
            result = Open(uri);
            return true;
        }
        catch
        {
            result = null;
            return false;
        }
    }

    public void ReadFrom(Uri uri)
    {
        if (!uri.IsFile) throw new NotSupportedException("Only file URIs are supported.");
        _mediaReader?.Dispose();
        _mediaReader = null;
        var reader = MediaReader.Open(uri.LocalPath, new(MediaMode.Audio));
        _mediaReader = Ref<MediaReader>.Create(reader);
        _uri = uri;
    }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            _mediaReader?.Dispose();
            GC.SuppressFinalize(this);
            IsDisposed = true;
        }
    }

    public SoundSource Clone()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        return new SoundSource { _mediaReader = _mediaReader?.Clone(), _uri = _uri };
    }

    public bool Read(int start, int length, [NotNullWhen(true)] out IPcm? sound)
    {
        if (IsDisposed || _mediaReader == null)
        {
            sound = null;
            return false;
        }

        return _mediaReader.Value.ReadAudio(start, length, out sound);
    }

    public bool Read(TimeSpan start, TimeSpan length, [NotNullWhen(true)] out IPcm? sound)
    {
        if (IsDisposed || _mediaReader == null)
        {
            sound = null;
            return false;
        }

        return _mediaReader.Value.ReadAudio(ToSamples(start), ToSamples(length), out sound);
    }

    public bool Read(TimeSpan start, int length, [NotNullWhen(true)] out IPcm? sound)
    {
        if (IsDisposed || _mediaReader == null)
        {
            sound = null;
            return false;
        }

        return _mediaReader.Value.ReadAudio(ToSamples(start), length, out sound);
    }

    public bool Read(int start, TimeSpan length, [NotNullWhen(true)] out IPcm? sound)
    {
        if (IsDisposed || _mediaReader == null)
        {
            sound = null;
            return false;
        }

        return _mediaReader.Value.ReadAudio(start, ToSamples(length), out sound);
    }

    ISoundSource ISoundSource.Clone() => Clone();

    public override bool Equals(object? obj)
    {
        return obj is SoundSource source
               && !IsDisposed && !source.IsDisposed
               && ReferenceEquals(_mediaReader?.Value, source._mediaReader?.Value);
    }

    public override int GetHashCode()
    {
        // ReSharper disable once NonReadonlyMemberInGetHashCode
        return HashCode.Combine(!IsDisposed ? _mediaReader?.Value : null);
    }

    private int ToSamples(TimeSpan timeSpan)
    {
        return (int)(timeSpan.TotalSeconds * SampleRate);
    }
}
