using Beutl.Media.Encoding;
using Beutl.Media.Music;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

public interface IFFmpegAudioWriter : IDisposable
{
    long NumberOfSamples { get; }

    AudioEncoderSettings AudioConfig { get; }

    bool AddAudio(IPcm sound);
}
