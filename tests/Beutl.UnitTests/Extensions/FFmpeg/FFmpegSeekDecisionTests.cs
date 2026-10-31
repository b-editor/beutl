using Beutl.Extensions.FFmpeg.Decoding;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
public class FFmpegSeekDecisionTests
{
    [TestCase(-1, 50, 0, 0)] // first still: do not decode unrequested frames
    [TestCase(50, 52, 0, 2)] // 60 fps source played at 30 fps
    [TestCase(52, 54, 2, 2)]
    [TestCase(54, 57, 2, 1)] // fractional frame rate: cover both advances without holes
    [TestCase(57, 59, 1, 1)]
    [TestCase(50, 54, 2, 2)] // increasing speed can retain already cached frames
    [TestCase(54, 54, 2, 2)] // repeated display does not reset playback
    [TestCase(54, 53, 2, 0)] // backwards scrubbing stops forward prefetch
    [TestCase(54, 155, 2, 0)] // a distant seek starts a new prediction
    [TestCase(155, 157, 0, 2)]
    public void PrefetchFollowsObservedForwardPlayback(int previous, int frame, int step, int expected)
    {
        Assert.That(FFmpegSeekDecision.GetPrefetchStep(previous, frame, step), Is.EqualTo(expected));
    }

    [TestCase(10, 0, 100L, 2, 12)]
    [TestCase(10, 2, 100L, 2, 16)]
    [TestCase(10, 0, 12L, 2, -1)] // stepped target reaches EOF
    [TestCase(10, 0, 100L, 0, -1)] // no playback observed
    [TestCase(int.MaxValue - 1, 1, long.MaxValue, 2, -1)] // no overflowing frame request
    public void PrefetchTargetsTheNextRequestedFrame(int frame, int cached, long total, int step, int expected)
    {
        bool found = FFmpegSeekDecision.HasPrefetchTarget(frame, cached, total, out int next, step);
        Assert.That(found, Is.EqualTo(expected >= 0));
        Assert.That(next, Is.EqualTo(expected));
    }

    [TestCase(true, 0L)] // current frame usable, request is the current position
    [TestCase(true, 1L)] // small forward skip handled by sequential grabbing
    [TestCase(true, 100L)] // exactly at the sequential-skip boundary
    public void ShouldReseek_False_WhenCurrentUsableAndSkipInRange(bool currentUsable, long skip)
    {
        Assert.That(FFmpegSeekDecision.ShouldReseek(currentUsable, skip), Is.False);
    }

    [TestCase(false, 0L)] // active frame unreferenced -> skip cannot be trusted
    [TestCase(false, 5L)]
    [TestCase(true, 101L)] // forward gap larger than the sequential-skip boundary
    [TestCase(true, -1L)] // request is behind the current position
    [TestCase(false, -1L)]
    public void ShouldReseek_True_WhenCurrentUnusableOrSkipOutOfRange(bool currentUsable, long skip)
    {
        Assert.That(FFmpegSeekDecision.ShouldReseek(currentUsable, skip), Is.True);
    }

    [TestCase(10, 0, 100L, 11)] // next frame right after base, well below EOF
    [TestCase(10, 3, 100L, 14)] // skip the cached run, target below EOF
    [TestCase(0, 0, 5L, 1)]
    [TestCase(10, 0, 12L, 11)] // boundary: nextFrame (11) < totalFrames (12)
    public void HasPrefetchTarget_True_WhenTargetBelowEof(int baseFrame, int cachedAhead, long totalFrames, int expectedNext)
    {
        bool result = FFmpegSeekDecision.HasPrefetchTarget(baseFrame, cachedAhead, totalFrames, out int nextFrame);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(nextFrame, Is.EqualTo(expectedNext));
        });
    }

    [TestCase(10, 0, 11L)] // nextFrame (11) == totalFrames -> at EOF
    [TestCase(10, 0, 5L)] // nextFrame past EOF
    [TestCase(10, 3, 14L)] // nextFrame (14) == totalFrames
    [TestCase(-1, 0, 100L)] // negative base: nothing requested yet
    [TestCase(-5, 2, 100L)]
    public void HasPrefetchTarget_False_AtOrPastEofOrNegativeBase(int baseFrame, int cachedAhead, long totalFrames)
    {
        bool result = FFmpegSeekDecision.HasPrefetchTarget(baseFrame, cachedAhead, totalFrames, out int nextFrame);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(nextFrame, Is.EqualTo(-1));
        });
    }
}
