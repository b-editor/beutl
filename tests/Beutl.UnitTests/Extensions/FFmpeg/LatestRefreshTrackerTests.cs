using Beutl.Extensions.FFmpeg.PropertyEditors;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
public class LatestRefreshTrackerTests
{
    [Test]
    public void StartNew_ReturnsLiveToken()
    {
        using var tracker = new LatestRefreshTracker();

        CancellationToken token = tracker.StartNew();

        Assert.That(LatestRefreshTracker.IsCurrent(token), Is.True);
    }

    [Test]
    public void StartNew_SupersedesPreviousToken()
    {
        // The anti-clobber invariant: a newer refresh request makes the previous one stale, so the
        // previous request's marshalled (async) result must be suppressed in favour of the latest.
        using var tracker = new LatestRefreshTracker();

        CancellationToken first = tracker.StartNew();
        CancellationToken second = tracker.StartNew();

        Assert.That(LatestRefreshTracker.IsCurrent(first), Is.False);
        Assert.That(LatestRefreshTracker.IsCurrent(second), Is.True);
    }

    [Test]
    public void Supersede_MakesCurrentTokenStale()
    {
        // The synchronous cache-hit / no-settings paths call Supersede() so any earlier in-flight
        // async request can no longer apply over the just-applied synchronous result.
        using var tracker = new LatestRefreshTracker();
        CancellationToken token = tracker.StartNew();

        tracker.Supersede();

        Assert.That(LatestRefreshTracker.IsCurrent(token), Is.False);
    }

    [Test]
    public void Dispose_MakesCurrentTokenStale()
    {
        var tracker = new LatestRefreshTracker();
        CancellationToken token = tracker.StartNew();

        tracker.Dispose();

        Assert.That(LatestRefreshTracker.IsCurrent(token), Is.False);
    }
}
