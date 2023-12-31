using System.Diagnostics.CodeAnalysis;

using Beutl.Media.Decoding;

namespace Beutl.Media.Source;

public sealed class VideoSource : IVideoSource
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

    public static VideoSource Open(string fileName)
    {
        var reader = MediaReader.Open(fileName, new(MediaMode.Video));
        return new VideoSource(Ref<MediaReader>.Create(reader), fileName);
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
        ObjectDisposedException.ThrowIf(IsDisposed, this);

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

    IVideoSource IVideoSource.Clone() => Clone();

    IMediaSource IMediaSource.Clone() => Clone();
}
