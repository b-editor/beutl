using Beutl.Media;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

public interface IFFmpegVideoWriter : IDisposable
{
    long NumberOfFrames { get; }

    FFmpegVideoEncoderSettings VideoConfig { get; }

    bool AddVideo(IBitmap image);
}
