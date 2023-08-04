using System.Diagnostics.CodeAnalysis;

namespace Beutl.Media.Source;

public interface IVideoSource : IMediaSource
{
    TimeSpan Duration { get; }

    PixelSize FrameSize { get; }

    bool Read(TimeSpan frame, [NotNullWhen(true)] out IBitmap? bitmap);

    bool Read(int frame, [NotNullWhen(true)] out IBitmap? bitmap);

    new IVideoSource Clone();

    IMediaSource IMediaSource.Clone() => Clone();
}
