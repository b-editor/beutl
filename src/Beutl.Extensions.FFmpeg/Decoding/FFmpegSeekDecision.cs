#if BEUTL_FFMPEG_WORKER
namespace Beutl.FFmpegWorker.Decoding;
#else
namespace Beutl.Extensions.FFmpeg.Decoding;
#endif

internal static class FFmpegSeekDecision
{
    // Largest forward gap (in frames) we are willing to reach by sequentially grabbing frames
    // instead of issuing a fresh seek. Beyond this, a seek is cheaper than decoding every frame.
    private const long MaxSequentialSkip = 100;

    // Force a fresh seek before serving the requested frame when:
    //   - currentUsable is false: the previous grab left the active frame unreferenced (a seek/grab
    //     that could not reach a decodable frame), so _videoNowFrame no longer describes a usable
    //     frame and the derived skip (which can be 0) must not be trusted.
    //   - skip < 0: the request is behind the current position, which sequential grabbing cannot reach.
    //   - skip > MaxSequentialSkip: the forward gap is large enough that seeking is cheaper.
    public static bool ShouldReseek(bool currentUsable, long skip)
        => !currentUsable || skip > MaxSequentialSkip || skip < 0;

    // Compute the next prefetch target and report whether one exists before EOF:
    //   - baseFrame < 0: no frame has been requested yet, so there is nothing to prefetch ahead of.
    //   - nextFrame = baseFrame + cachedAhead + 1: the first frame after the already-cached run.
    //   - nextFrame >= totalFrames: the target is at or past EOF, so there is nothing to prefetch.
    // When this returns false, nextFrame is set to -1 and the caller should stop advancing.
    public static bool HasPrefetchTarget(int baseFrame, int cachedAhead, long totalFrames, out int nextFrame)
    {
        if (baseFrame < 0)
        {
            nextFrame = -1;
            return false;
        }

        nextFrame = baseFrame + cachedAhead + 1;
        if (nextFrame >= totalFrames)
        {
            nextFrame = -1;
            return false;
        }

        return true;
    }
}
