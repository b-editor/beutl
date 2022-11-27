using System.Diagnostics.CodeAnalysis;

namespace Beutl.Media.Source;

public interface IImageSource : IMediaSource
{
    PixelSize FrameSize { get; }

    bool Read([NotNullWhen(true)] out IBitmap? bitmap);
}
