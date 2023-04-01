using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Beutl.Media.Source;

[JsonConverter(typeof(ImageSourceJsonConverter))]
public interface IImageSource : IMediaSource
{
    PixelSize FrameSize { get; }

    bool Read([NotNullWhen(true)] out IBitmap? bitmap);

    new IImageSource Clone();

    IMediaSource IMediaSource.Clone() => Clone();
}
