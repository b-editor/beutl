using Beutl.Extensions.FFmpeg.Decoding;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
public class FFmpegReaderIdleTrackerTests
{
    private const long Now = 100_000;

    private static FFmpegReaderIdleTracker CreateTracker(int maxIdleReaders = 1)
        => new(new FFmpegReaderIdlePolicy.Limits(5000, maxIdleReaders, long.MaxValue), sweepInterval: null);

    private static readonly FFmpegReaderIdlePolicy.Limits s_suspendImmediately = new(0, 0, 0);

    [Test]
    public void PeriodicSweep_SuspendsIdleReaders()
    {
        using var tracker = new FFmpegReaderIdleTracker(s_suspendImmediately, TimeSpan.FromMilliseconds(10));
        var reader = new FakeReader(Environment.TickCount64);
        tracker.Track(reader);

        Assert.That(() => reader.IsSuspended, Is.True.After(5000, 10));
        Assert.That(() => tracker.TrackedCount, Is.Zero.After(5000, 10));
    }

    [Test]
    public void PeriodicSweep_KeepsRunningAfterASweepThrows()
    {
        using var tracker = new FFmpegReaderIdleTracker(s_suspendImmediately, TimeSpan.FromMilliseconds(10));
        var reader = new FakeReader(Environment.TickCount64) { ThrowOnFirstSuspend = true };
        tracker.Track(reader);

        Assert.That(() => reader.IsSuspended, Is.True.After(5000, 10));
        Assert.That(reader.SuspendAttempts, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void Dispose_StopsARunningPeriodicSweep()
    {
        var tracker = new FFmpegReaderIdleTracker(s_suspendImmediately, TimeSpan.FromMilliseconds(10));
        // A busy reader declines every suspension, so it stays tracked and each tick sweeps it again.
        var reader = new FakeReader(Environment.TickCount64) { Busy = true };
        tracker.Track(reader);
        Assert.That(() => reader.SuspendAttempts, Is.GreaterThan(0).After(5000, 10), "the sweep must be running");

        // Dispose waits for a sweep in progress, so the count is final once it returns.
        tracker.Dispose();
        int attempts = reader.SuspendAttempts;
        Thread.Sleep(100);

        Assert.That(reader.SuspendAttempts, Is.EqualTo(attempts));
    }

    [Test]
    public void Track_AfterDispose_DoesNotStartTheSweep()
    {
        var tracker = new FFmpegReaderIdleTracker(s_suspendImmediately, TimeSpan.FromMilliseconds(10));
        tracker.Dispose();
        var reader = new FakeReader(Environment.TickCount64);
        tracker.Track(reader);

        Thread.Sleep(100);

        Assert.That(reader.SuspendAttempts, Is.Zero);
    }

    [Test]
    public void Sweep_SuspendsReadersSelectedByPolicy_AndStopsTrackingThem()
    {
        var tracker = CreateTracker();
        var recent = new FakeReader(Now - 6_000);
        var old = new FakeReader(Now - 60_000);
        var active = new FakeReader(Now - 100);
        tracker.Track(recent);
        tracker.Track(old);
        tracker.Track(active);

        tracker.Sweep(Now);

        Assert.Multiple(() =>
        {
            Assert.That(old.IsSuspended, Is.True);
            Assert.That(recent.IsSuspended, Is.False);
            Assert.That(active.IsSuspended, Is.False);
            Assert.That(tracker.TrackedCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void Sweep_PassesObservedAccessTime_SoAReaderUsedMeanwhileIsNotSuspended()
    {
        var tracker = CreateTracker(maxIdleReaders: 0);
        var reader = new FakeReader(Now - 60_000) { AccessDuringSuspend = Now };
        tracker.Track(reader);

        tracker.Sweep(Now);

        Assert.Multiple(() =>
        {
            Assert.That(reader.IsSuspended, Is.False);
            Assert.That(tracker.TrackedCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Sweep_KeepsTrackingReaderThatDeclinesSuspension()
    {
        var tracker = CreateTracker(maxIdleReaders: 0);
        var busy = new FakeReader(Now - 60_000) { Busy = true };
        tracker.Track(busy);

        tracker.Sweep(Now);

        Assert.That(tracker.TrackedCount, Is.EqualTo(1));
    }

    [Test]
    public void Untrack_RemovesReader()
    {
        var tracker = CreateTracker(maxIdleReaders: 0);
        var reader = new FakeReader(Now - 60_000);
        tracker.Track(reader);
        tracker.Untrack(reader);

        tracker.Sweep(Now);

        Assert.Multiple(() =>
        {
            Assert.That(reader.IsSuspended, Is.False);
            Assert.That(tracker.TrackedCount, Is.Zero);
        });
    }

    // Periodic sweeps call TrySuspend on a timer thread while the test reads the state.
    private sealed class FakeReader(long lastAccessTicks) : IIdleSuspendableReader
    {
        private int _suspendAttempts;
        private volatile bool _isSuspended;

        public long LastAccessTicks { get; private set; } = lastAccessTicks;

        public long PixelCount => 1920L * 1080;

        public bool IsSuspended => _isSuspended;

        public bool Busy { get; init; }

        public long? AccessDuringSuspend { get; init; }

        public bool ThrowOnFirstSuspend { get; init; }

        public int SuspendAttempts => Volatile.Read(ref _suspendAttempts);

        public bool TrySuspend(long expectedLastAccessTicks)
        {
            if (Interlocked.Increment(ref _suspendAttempts) == 1 && ThrowOnFirstSuspend)
                throw new InvalidOperationException("Simulated suspension failure.");

            if (AccessDuringSuspend is { } accessed)
                LastAccessTicks = accessed;

            if (Busy || _isSuspended || LastAccessTicks != expectedLastAccessTicks)
                return false;

            _isSuspended = true;
            return true;
        }
    }
}
