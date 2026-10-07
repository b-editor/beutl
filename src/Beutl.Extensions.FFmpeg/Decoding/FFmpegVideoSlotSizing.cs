#if BEUTL_FFMPEG_WORKER
namespace Beutl.FFmpegWorker.Decoding;
#else
namespace Beutl.Extensions.FFmpeg.Decoding;
#endif

internal static class FFmpegVideoSlotSizing
{
    private const int SlotPadding = 64;

    // SDR frames are delivered as BGRA and HDR frames as RGBA64LE.
    public static int GetBytesPerPixel(bool isHdr) => isHdr ? 8 : 4;

    // Ring buffer slots are allocated up front and Windows commits the whole mapping at creation, so a
    // slot must be sized for the reader's actual pixel format rather than the HDR worst case.
    public static long GetSlotSize(int width, int height, bool isHdr)
        => (long)width * height * GetBytesPerPixel(isHdr) + SlotPadding;
}
