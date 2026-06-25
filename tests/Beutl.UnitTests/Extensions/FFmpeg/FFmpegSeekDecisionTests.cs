using Beutl.Extensions.FFmpeg.Decoding;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
public class FFmpegSeekDecisionTests
{
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
