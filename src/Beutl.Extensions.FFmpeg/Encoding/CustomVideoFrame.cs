using System.Runtime.InteropServices;

using Beutl.Media;
using Beutl.Media.Pixel;

using FFMpegCore.Pipes;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

internal sealed class CustomVideoFrame(Bitmap<Bgra8888> bitmap) : IVideoFrame, IDisposable
{
    public int Width => bitmap.Width;

    public int Height => bitmap.Height;

    public string Format => "bgra";

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
