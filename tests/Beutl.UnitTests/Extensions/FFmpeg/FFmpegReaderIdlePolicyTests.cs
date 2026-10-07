using Beutl.Extensions.FFmpeg.Decoding;
using Candidate = Beutl.Extensions.FFmpeg.Decoding.FFmpegReaderIdlePolicy.Candidate;
using Limits = Beutl.Extensions.FFmpeg.Decoding.FFmpegReaderIdlePolicy.Limits;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
public class FFmpegReaderIdlePolicyTests
{
    private const long Now = 100_000;
    private const long Bytes1080p = 1920L * 1080 * 4;

    private static readonly Limits s_limits = new(IdleGraceMilliseconds: 5000, MaxIdleReaders: 2, MaxIdleBytes: 3 * Bytes1080p);

    [Test]
    public void ReadersWithinGracePeriod_AreNeverSuspended()
    {
        // Ten readers rendered in the last few seconds (e.g. a multi-layer frame) exceed both budgets
        // but are all in use, so none may be suspended.
        Candidate[] readers = Enumerable.Range(0, 10)
            .Select(i => new Candidate(Now - 4999 + i, Bytes1080p))
            .ToArray();

        Assert.That(FFmpegReaderIdlePolicy.SelectForSuspension(readers, Now, s_limits), Is.Empty);
    }

    [Test]
    public void IdleReadersBeyondCountBudget_SuspendsLeastRecentlyUsed()
    {
        Candidate[] readers =
        [
            new(Now - 30_000, Bytes1080p), // oldest
            new(Now - 6_000, Bytes1080p), // most recent idle
            new(Now - 20_000, Bytes1080p),
            new(Now - 10_000, Bytes1080p),
        ];

        Assert.That(
            FFmpegReaderIdlePolicy.SelectForSuspension(readers, Now, s_limits),
            Is.EquivalentTo(new[] { 0, 2 }));
    }

    [Test]
    public void IdleReadersBeyondByteBudget_AreSuspended()
    {
        Candidate[] readers =
        [
            new(Now - 6_000, 2 * Bytes1080p),
            new(Now - 7_000, 2 * Bytes1080p), // would exceed 3 x 1080p together with the first
        ];

        Assert.That(FFmpegReaderIdlePolicy.SelectForSuspension(readers, Now, s_limits), Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public void ReaderLargerThanByteBudget_IsSuspendedOnceIdle()
    {
        Candidate[] readers = [new(Now - 6_000, 4 * Bytes1080p)];

        Assert.That(FFmpegReaderIdlePolicy.SelectForSuspension(readers, Now, s_limits), Is.EqualTo(new[] { 0 }));
    }

    // HDR ring buffers take 8 bytes per pixel, so the same budget keeps half as many HDR readers warm.
    [Test]
    public void DefaultLimits_KeepFour4KSdrReaders_ButOnlyTwo4KHdrReaders()
    {
        long sdrReader = 4 * FFmpegVideoSlotSizing.GetSlotSize(3840, 2160, isHdr: false);
        long hdrReader = 4 * FFmpegVideoSlotSizing.GetSlotSize(3840, 2160, isHdr: true);

        static Candidate[] SixIdleReaders(long bytes)
            => Enumerable.Range(0, 6).Select(i => new Candidate(Now - 10_000 - i, bytes)).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(
                FFmpegReaderIdlePolicy.SelectForSuspension(SixIdleReaders(sdrReader), Now, FFmpegReaderIdlePolicy.DefaultLimits),
                Has.Count.EqualTo(2));
            Assert.That(
                FFmpegReaderIdlePolicy.SelectForSuspension(SixIdleReaders(hdrReader), Now, FFmpegReaderIdlePolicy.DefaultLimits),
                Has.Count.EqualTo(4));
        });
    }

    [Test]
    public void ActiveReaders_DoNotConsumeIdleBudget()
    {
        Candidate[] readers =
        [
            new(Now - 100, Bytes1080p),
            new(Now - 200, Bytes1080p),
            new(Now - 300, Bytes1080p),
            new(Now - 6_000, Bytes1080p),
            new(Now - 7_000, Bytes1080p),
        ];

        Assert.That(FFmpegReaderIdlePolicy.SelectForSuspension(readers, Now, s_limits), Is.Empty);
    }
}
