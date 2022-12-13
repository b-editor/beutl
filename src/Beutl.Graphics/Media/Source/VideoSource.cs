using System.Diagnostics.CodeAnalysis;

using Beutl.Media.Decoding;

namespace Beutl.Media.Source;

public sealed class VideoSource : IVideoSource, IImageSource
{
    private readonly Ref<MediaReader> _mediaReader;
    private readonly double _frameRate;

    public VideoSource(Ref<MediaReader> mediaReader, string fileName)
    {
        _mediaReader = mediaReader;
        Name = fileName;
        Duration = TimeSpan.FromSeconds(mediaReader.Value.VideoInfo.Duration.ToDouble());
        FrameSize = mediaReader.Value.VideoInfo.FrameSize;
        FrameRate = mediaReader.Value.VideoInfo.FrameRate;
        _frameRate = FrameRate.ToDouble();
    }

    ~VideoSource()
    {
        Dispose();
    }

    public TimeSpan Duration { get; }
    
    public Rational FrameRate { get; }

    public PixelSize FrameSize { get; }

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

    public VideoSource Clone()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(VideoSource));

        return new VideoSource(_mediaReader.Clone(), Name);
    }

    public bool Read(TimeSpan frame, [NotNullWhen(true)] out IBitmap? bitmap)
    {
        if (IsDisposed)
        {
            bitmap = null;
            return false;
        }

        double frameNum = frame.TotalSeconds * _frameRate;
        return _mediaReader.Value.ReadVideo((int)frameNum, out bitmap);
    }
    
    public bool Read(int frame, [NotNullWhen(true)] out IBitmap? bitmap)
    {
        if (IsDisposed)
        {
            bitmap = null;
            return false;
        }

        return _mediaReader.Value.ReadVideo(frame, out bitmap);
    }

    public bool Read([NotNullWhen(true)] out IBitmap? bitmap)
    {
        return Read(0, out bitmap);
    }

    IImageSource IImageSource.Clone() => Clone();

    IVideoSource IVideoSource.Clone() => Clone();

    IMediaSource IMediaSource.Clone() => Clone();
}
