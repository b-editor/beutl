#if BEUTL_FFMPEG_WORKER
namespace Beutl.FFmpegWorker.Decoding;
#else
namespace Beutl.Extensions.FFmpeg.Decoding;
#endif

internal static class FFmpegFrameValidation
{
    // AV_PIX_FMT_NONE. A decode that ends on EAGAIN/EOF leaves the frame unreferenced
    // (avcodec_receive_frame unrefs before output), resetting format here and size to 0.
    private const int PixelFormatNone = -1;

    // Such an unreferenced frame makes FFmpeg's buffersrc fail with AVERROR(EINVAL)
    // ("Unspecified pixel format"), so callers must drop it rather than build a graph from it.
    public static bool IsUsableVideoFrame(int pixelFormat, int width, int height)
        => pixelFormat != PixelFormatNone && width > 0 && height > 0;
}
