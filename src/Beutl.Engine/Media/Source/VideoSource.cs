using System.Diagnostics.CodeAnalysis;
using Beutl.Media.Decoding;

namespace Beutl.Media.Source;

public sealed class VideoSource : IVideoSource
{
    private Ref<MediaReader>? _mediaReader;
    private Uri? _uri;

    public VideoSource()
    {
    }

    ~VideoSource()
    {
        Dispose();
    }

    public TimeSpan Duration =>
        _mediaReader?.Value.VideoInfo.Duration != null
            ? TimeSpan.FromSeconds(_mediaReader.Value.VideoInfo.Duration.ToDouble())
            : throw new InvalidOperationException("MediaReader is not set.");

    public Rational FrameRate => _mediaReader?.Value.VideoInfo.FrameRate ??
                                 throw new InvalidOperationException("MediaReader is not set.");

    public PixelSize FrameSize => _mediaReader?.Value.VideoInfo.FrameSize ??
                                  throw new InvalidOperationException("MediaReader is not set.");

    public bool IsDisposed { get; private set; }

    public Uri Uri => _uri ?? throw new InvalidOperationException("URI is not set.");

    public static VideoSource Open(string fileName)
    {
        var reader = MediaReader.Open(fileName, new(MediaMode.Video));
        return new VideoSource
        {
            _mediaReader = Ref<MediaReader>.Create(reader),
            _uri = new Uri(new Uri("file://"), fileName)
        };
    }

    public static bool TryOpen(string fileName, out VideoSource? result)
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

    public void ReadFrom(Uri uri)
    {
        if (!uri.IsFile) throw new NotSupportedException("Only file URIs are supported.");

        _mediaReader?.Dispose();
        _mediaReader = null;
        var reader = MediaReader.Open(uri.LocalPath, new(MediaMode.Video));
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

    public VideoSource Clone()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        return new VideoSource { _mediaReader = _mediaReader?.Clone(), _uri = _uri };
    }

    public bool Read(TimeSpan frame, [NotNullWhen(true)] out IBitmap? bitmap)
    {
        if (IsDisposed || _mediaReader == null)
        {
            bitmap = null;
            return false;
        }

        double frameRate = FrameRate.ToDouble();
        double frameNum = frame.TotalSeconds * frameRate;
        return _mediaReader.Value.ReadVideo((int)frameNum, out bitmap);
    }

    public bool Read(int frame, [NotNullWhen(true)] out IBitmap? bitmap)
    {
        if (IsDisposed || _mediaReader == null)
        {
            bitmap = null;
            return false;
        }

        return _mediaReader.Value.ReadVideo(frame, out bitmap);
    }

    public override bool Equals(object? obj)
    {
        return obj is VideoSource source
               && !IsDisposed && !source.IsDisposed
               && ReferenceEquals(_mediaReader?.Value, source._mediaReader?.Value);
    }

    public override int GetHashCode()
    {
        // ReSharper disable once NonReadonlyMemberInGetHashCode
        return HashCode.Combine(!IsDisposed ? _mediaReader?.Value : null);
    }

    IVideoSource IVideoSource.Clone() => Clone();

    IMediaSource IMediaSource.Clone() => Clone();
}
