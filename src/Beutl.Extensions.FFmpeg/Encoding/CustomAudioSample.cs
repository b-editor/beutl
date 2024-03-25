using System.Runtime.InteropServices;

using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Pixel;

using FFMpegCore.Pipes;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

internal sealed class CustomAudioSample(Pcm<Stereo32BitFloat> bitmap) : IAudioSample, IDisposable
{
    public void Dispose()
    {
        bitmap.Dispose();
    }

    public void Serialize(Stream pipe)
    {
        pipe.Write(MemoryMarshal.AsBytes(bitmap.DataSpan));
    }

    public async Task SerializeAsync(Stream pipe, CancellationToken token)
    {
        await pipe.WriteAsync(MemoryMarshal.AsBytes(bitmap.DataSpan).ToArray(), token).ConfigureAwait(false);
    }
}
