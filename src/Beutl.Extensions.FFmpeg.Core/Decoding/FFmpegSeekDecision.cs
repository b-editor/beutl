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

    // Start prefetching after forward reads establish a direction. The common divisor keeps
    // fractional rates (e.g. alternating 2/3-frame advances) covered without repeatedly seeking
    // backwards to fill holes. A seek resets the prediction; a repeated frame retains it.
    public static int GetPrefetchStep(int previousFrame, int frame, int currentStep)
    {
        long skip = (long)frame - previousFrame;
        if (skip == 0)
            return currentStep;
        if (previousFrame < 0 || skip < 0 || ShouldReseek(true, skip))
            return 0;

        int step = (int)skip;
        while (currentStep > 0)
        {
            (step, currentStep) = (currentStep, step % currentStep);
        }

        return step;
    }

    // Compute the next prefetch target and report whether one exists before EOF:
    //   - baseFrame < 0: no frame has been requested yet, so there is nothing to prefetch ahead of.
    //   - step <= 0: no forward playback has been observed yet.
    //   - target = baseFrame + (cachedAhead + 1) * step: the first uncached playback frame.
    //   - nextFrame >= totalFrames: the target is at or past EOF, so there is nothing to prefetch.
    // When this returns false, nextFrame is set to -1 and the caller should stop advancing.
    public static bool HasPrefetchTarget(int baseFrame, int cachedAhead, long totalFrames, out int nextFrame, int step = 1)
    {
        long target = (long)baseFrame + ((long)cachedAhead + 1) * step;
        if (baseFrame < 0 || step <= 0 || target >= totalFrames || target > int.MaxValue)
        {
            nextFrame = -1;
            return false;
        }

        nextFrame = (int)target;
        return true;
    }
}
