using Beutl.Extensions.FFmpeg.Decoding;
using Candidate = Beutl.Extensions.FFmpeg.Decoding.FFmpegReaderIdlePolicy.Candidate;
using Limits = Beutl.Extensions.FFmpeg.Decoding.FFmpegReaderIdlePolicy.Limits;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
public class FFmpegReaderIdlePolicyTests
{
    private const long Now = 100_000;
    private const long Pixels1080p = 1920L * 1080;

    private static readonly Limits s_limits = new(IdleGraceMilliseconds: 5000, MaxIdleReaders: 2, MaxIdlePixels: 3 * Pixels1080p);

    [Test]
    public void ReadersWithinGracePeriod_AreNeverSuspended()
    {
        // Ten readers rendered in the last few seconds (e.g. a multi-layer frame) exceed both budgets
        // but are all in use, so none may be suspended.
        Candidate[] readers = Enumerable.Range(0, 10)
            .Select(i => new Candidate(Now - 4999 + i, Pixels1080p))
            .ToArray();

        Assert.That(FFmpegReaderIdlePolicy.SelectForSuspension(readers, Now, s_limits), Is.Empty);
    }

    [Test]
    public void IdleReadersBeyondCountBudget_SuspendsLeastRecentlyUsed()
    {
        Candidate[] readers =
        [
            new(Now - 30_000, Pixels1080p), // oldest
            new(Now - 6_000, Pixels1080p), // most recent idle
            new(Now - 20_000, Pixels1080p),
            new(Now - 10_000, Pixels1080p),
        ];

        Assert.That(
            FFmpegReaderIdlePolicy.SelectForSuspension(readers, Now, s_limits),
            Is.EquivalentTo(new[] { 0, 2 }));
    }

    [Test]
    public void IdleReadersBeyondPixelBudget_AreSuspended()
    {
        Candidate[] readers =
        [
            new(Now - 6_000, 2 * Pixels1080p),
            new(Now - 7_000, 2 * Pixels1080p), // would exceed 3 x 1080p together with the first
        ];

        Assert.That(FFmpegReaderIdlePolicy.SelectForSuspension(readers, Now, s_limits), Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public void ReaderLargerThanPixelBudget_IsSuspendedOnceIdle()
    {
        Candidate[] readers = [new(Now - 6_000, 4 * Pixels1080p)];

        Assert.That(FFmpegReaderIdlePolicy.SelectForSuspension(readers, Now, s_limits), Is.EqualTo(new[] { 0 }));
    }

    [Test]
    public void ActiveReaders_DoNotConsumeIdleBudget()
    {
        Candidate[] readers =
        [
            new(Now - 100, Pixels1080p),
            new(Now - 200, Pixels1080p),
            new(Now - 300, Pixels1080p),
            new(Now - 6_000, Pixels1080p),
            new(Now - 7_000, Pixels1080p),
        ];

        Assert.That(FFmpegReaderIdlePolicy.SelectForSuspension(readers, Now, s_limits), Is.Empty);
    }
}
