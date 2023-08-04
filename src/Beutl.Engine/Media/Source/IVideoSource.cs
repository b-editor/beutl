using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Beutl.Media.Source;

[JsonConverter(typeof(VideoSourceJsonConverter))]
public interface IVideoSource : IMediaSource
{
    TimeSpan Duration { get; }

    Rational FrameRate { get; }

    PixelSize FrameSize { get; }

    bool Read(TimeSpan frame, [NotNullWhen(true)] out IBitmap? bitmap);

    bool Read(int frame, [NotNullWhen(true)] out IBitmap? bitmap);

    new IVideoSource Clone();

    IMediaSource IMediaSource.Clone() => Clone();
}
