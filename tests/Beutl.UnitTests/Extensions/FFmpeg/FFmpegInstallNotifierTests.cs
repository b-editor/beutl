using System.Collections.Concurrent;
using Beutl.Extensions.FFmpeg;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
public class FFmpegInstallNotifierTests
{
    [SetUp]
    public void ResetState()
    {
        // MarkInstalled clears the throttle slot (Interlocked.Exchange to 0).
        FFmpegInstallNotifier.MarkInstalled();
    }

    [Test]
    public void TryAcquireNotifySlot_FirstCall_Wins()
    {
        Assert.That(FFmpegInstallNotifier.TryAcquireNotifySlot(now: 1_000), Is.True);
    }

    [Test]
    public void TryAcquireNotifySlot_SecondCallWithinWindow_Loses()
    {
        Assert.That(FFmpegInstallNotifier.TryAcquireNotifySlot(now: 1_000), Is.True);
        Assert.That(FFmpegInstallNotifier.TryAcquireNotifySlot(now: 1_000), Is.False);
        Assert.That(FFmpegInstallNotifier.TryAcquireNotifySlot(now: 5_000), Is.False);
    }

    [Test]
    public void TryAcquireNotifySlot_AfterWindowElapsed_WinsAgain()
    {
        const long throttleMs = 10_000;
        Assert.That(FFmpegInstallNotifier.TryAcquireNotifySlot(now: 1_000), Is.True);
        Assert.That(FFmpegInstallNotifier.TryAcquireNotifySlot(now: 1_000 + throttleMs), Is.True);
    }

    [Test]
    public void AvailabilityChanged_FiresWhenMissingStateChanges()
    {
        int changes = 0;
        void OnAvailabilityChanged(object? sender, EventArgs e) => changes++;

        FFmpegInstallNotifier.AvailabilityChanged += OnAvailabilityChanged;
        try
        {
            FFmpegInstallNotifier.MarkMissing();
            FFmpegInstallNotifier.MarkMissing();
            FFmpegInstallNotifier.MarkInstalled();
        }
        finally
        {
            FFmpegInstallNotifier.AvailabilityChanged -= OnAvailabilityChanged;
        }

        Assert.That(changes, Is.EqualTo(2));
    }

    [Test]
    public void MarkVerificationStarted_ClearsMissingWithoutAvailabilitySignal()
    {
        FFmpegInstallNotifier.MarkMissing();
        int changes = 0;
        void OnAvailabilityChanged(object? sender, EventArgs e) => changes++;

        FFmpegInstallNotifier.AvailabilityChanged += OnAvailabilityChanged;
        try
        {
            FFmpegInstallNotifier.MarkVerificationStarted();
            Assert.Multiple(() =>
            {
                Assert.That(FFmpegInstallNotifier.IsLibrariesMissing, Is.False);
                Assert.That(changes, Is.EqualTo(0));
            });

            FFmpegInstallNotifier.MarkInstalled();
        }
        finally
        {
            FFmpegInstallNotifier.AvailabilityChanged -= OnAvailabilityChanged;
        }

        Assert.That(changes, Is.EqualTo(1));
    }

    [Test]
    public void ShouldSkipStartProbe_SkipsWithinCooldownThenAllowsAfterElapsed()
    {
        const long cooldownMs = 30_000; // mirrors FFmpegLibraryState.ReprobeCooldownMs

        FFmpegInstallNotifier.MarkMissing();
        long since = FFmpegInstallNotifier.MissingSinceTicks;

        Assert.Multiple(() =>
        {
            Assert.That(since, Is.Not.EqualTo(0), "MarkMissing must arm the re-probe cooldown");
            Assert.That(FFmpegInstallNotifier.ShouldSkipStartProbe(since + 1_000), Is.True);
            Assert.That(FFmpegInstallNotifier.ShouldSkipStartProbe(since + cooldownMs), Is.False);
            Assert.That(FFmpegInstallNotifier.ShouldSkipStartProbe(since + cooldownMs + 1_000), Is.False);
        });
    }

    [Test]
    public void ShouldSkipStartProbe_RepeatedProbesWithinCooldown_AllShortCircuit()
    {
        // A burst of decode attempts inside the cooldown window must all short-circuit (no worker
        // re-probe). This is what suppresses the hundreds-of-errors-per-session pattern: only the
        // first attempt in the window pays for a real worker start.
        FFmpegInstallNotifier.MarkMissing();
        long since = FFmpegInstallNotifier.MissingSinceTicks;

        for (long t = since; t < since + 25_000; t += 1_000)
        {
            Assert.That(FFmpegInstallNotifier.ShouldSkipStartProbe(t), Is.True,
                $"expected a short-circuit at t={t - since}ms into the cooldown");
        }
    }

    [Test]
    public void RecordMissingObserved_FirstObservation_ReturnsFalse()
    {
        FFmpegInstallNotifier.MarkInstalled();

        Assert.Multiple(() =>
        {
            Assert.That(FFmpegInstallNotifier.RecordMissingObserved(), Is.False,
                "the first observation is a genuine discovery, so callers must log it as an error");
            Assert.That(FFmpegInstallNotifier.IsLibrariesMissing, Is.True);
            Assert.That(FFmpegInstallNotifier.MissingSinceTicks, Is.EqualTo(0),
                "an observe-only decode failure must not arm the re-probe cooldown");
        });
    }

    [Test]
    public void RecordMissingObserved_SecondObservation_ReturnsTrue()
    {
        FFmpegInstallNotifier.MarkInstalled();
        FFmpegInstallNotifier.RecordMissingObserved();

        Assert.That(FFmpegInstallNotifier.RecordMissingObserved(), Is.True,
            "a later observation is an expected short-circuit, so callers must fail quietly");
    }

    [Test]
    public void RecordMissingObserved_DoesNotPushCooldownWindowForward()
    {
        FFmpegInstallNotifier.MarkMissing();
        long since = FFmpegInstallNotifier.MissingSinceTicks;

        FFmpegInstallNotifier.RecordMissingObserved();

        Assert.That(FFmpegInstallNotifier.MissingSinceTicks, Is.EqualTo(since),
            "a short-circuited decode must not re-arm and push the cooldown window forward");
    }

    [Test]
    public void NotifyMissing_DoesNotPushActiveCooldownWindowForward()
    {
        FFmpegInstallNotifier.MarkMissing();
        long since = FFmpegInstallNotifier.MissingSinceTicks;

        FFmpegInstallNotifier.NotifyMissing();

        Assert.That(FFmpegInstallNotifier.MissingSinceTicks, Is.EqualTo(since),
            "a short-circuited encoding retry must not re-arm the cooldown");
    }

    [Test]
    public void RecordMissingObserved_UnderConcurrency_OnlyOneFirstObservation()
    {
        const int iterations = 25;
        const int threads = 64;

        for (int i = 0; i < iterations; i++)
        {
            FFmpegInstallNotifier.MarkInstalled();

            int firstObservations = 0;
            using var barrier = new Barrier(threads);
            var tasks = new Task[threads];
            for (int t = 0; t < threads; t++)
            {
                tasks[t] = Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    if (!FFmpegInstallNotifier.RecordMissingObserved())
                        Interlocked.Increment(ref firstObservations);
                });
            }

            Task.WaitAll(tasks);
            Assert.That(firstObservations, Is.EqualTo(1),
                $"iteration {i}: expected exactly one first observation");
        }
    }

    [Test]
    public void AvailabilityChanged_DoesNotDeadlockWhenSubscriberDispatchesTransition()
    {
        FFmpegInstallNotifier.MarkInstalled();
        using var firstNotificationEntered = new ManualResetEventSlim();
        var observedStates = new List<bool>();
        int callbackCount = 0;

        void OnAvailabilityChanged(object? sender, EventArgs e)
        {
            observedStates.Add(((FFmpegLibraryAvailabilityChangedEventArgs)e).IsLibrariesMissing);
            if (Interlocked.Increment(ref callbackCount) == 1)
            {
                firstNotificationEntered.Set();
                FFmpegInstallNotifier.MarkInstalled();
            }
        }

        FFmpegInstallNotifier.AvailabilityChanged += OnAvailabilityChanged;
        try
        {
            Task first = Task.Run(FFmpegInstallNotifier.MarkMissing);
            Assert.That(firstNotificationEntered.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "the first availability notification did not start");

            Assert.That(first.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "the state transition must complete while its subscriber is dispatching another transition");
            Assert.Multiple(() =>
            {
                Assert.That(FFmpegInstallNotifier.IsLibrariesMissing, Is.False);
                Assert.That(Volatile.Read(ref callbackCount), Is.EqualTo(2),
                    "both transitions must raise AvailabilityChanged");
                Assert.That(observedStates, Is.EqualTo(new[] { true, false }),
                    "reentrant transitions must be delivered in callback order");
            });
        }
        finally
        {
            FFmpegInstallNotifier.AvailabilityChanged -= OnAvailabilityChanged;
            FFmpegInstallNotifier.MarkInstalled();
        }
    }

    [Test]
    public void AvailabilityChanged_PropagatesExceptionToOwningTransition()
    {
        FFmpegInstallNotifier.MarkInstalled();
        using var firstNotificationEntered = new ManualResetEventSlim();
        using var releaseFirstNotification = new ManualResetEventSlim();
        using var secondTransitionStarted = new ManualResetEventSlim();
        var expected = new InvalidOperationException("installed callback failed");

        void OnAvailabilityChanged(object? sender, EventArgs e)
        {
            if (((FFmpegLibraryAvailabilityChangedEventArgs)e).IsLibrariesMissing)
            {
                firstNotificationEntered.Set();
                releaseFirstNotification.Wait();
            }
            else
            {
                throw expected;
            }
        }

        FFmpegInstallNotifier.AvailabilityChanged += OnAvailabilityChanged;
        try
        {
            Task first = Task.Run(FFmpegInstallNotifier.MarkMissing);
            Assert.That(firstNotificationEntered.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "the first availability notification did not start");

            Task second = Task.Run(() =>
            {
                secondTransitionStarted.Set();
                FFmpegInstallNotifier.MarkInstalled();
            });
            Assert.That(secondTransitionStarted.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "the second transition did not start");
            Assert.That(second.Wait(TimeSpan.FromMilliseconds(100)), Is.False,
                "the second transition should wait for its queued notification");

            releaseFirstNotification.Set();
            Assert.That(first.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "the dispatcher must not receive another caller's subscriber exception");
            Assert.That(SpinWait.SpinUntil(() => second.IsCompleted, TimeSpan.FromSeconds(5)), Is.True,
                "the owning transition did not complete");
            Assert.That(second.IsFaulted, Is.True);
            Assert.That(second.Exception!.GetBaseException(), Is.SameAs(expected));
        }
        finally
        {
            releaseFirstNotification.Set();
            FFmpegInstallNotifier.AvailabilityChanged -= OnAvailabilityChanged;
            FFmpegInstallNotifier.MarkInstalled();
        }
    }

    [Test]
    public void AvailabilityChanged_PropagatesSubscriberException()
    {
        FFmpegInstallNotifier.MarkInstalled();
        var expected = new InvalidOperationException("availability callback failed");

        void OnAvailabilityChanged(object? sender, EventArgs e) => throw expected;

        FFmpegInstallNotifier.AvailabilityChanged += OnAvailabilityChanged;
        try
        {
            InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
                FFmpegInstallNotifier.MarkMissing);
            Assert.That(actual, Is.SameAs(expected));
        }
        finally
        {
            FFmpegInstallNotifier.AvailabilityChanged -= OnAvailabilityChanged;
            FFmpegInstallNotifier.MarkInstalled();
        }
    }

    [Test]
    public void AvailabilityChanged_ReportsQueuedTransitionSnapshot()
    {
        FFmpegInstallNotifier.MarkInstalled();
        using var firstNotificationEntered = new ManualResetEventSlim();
        using var releaseFirstNotification = new ManualResetEventSlim();
        var observedStates = new ConcurrentQueue<bool>();
        int callbackCount = 0;

        void OnAvailabilityChanged(object? sender, EventArgs e)
        {
            observedStates.Enqueue(((FFmpegLibraryAvailabilityChangedEventArgs)e).IsLibrariesMissing);
            if (Interlocked.Increment(ref callbackCount) == 1)
            {
                firstNotificationEntered.Set();
                releaseFirstNotification.Wait();
            }
        }

        FFmpegInstallNotifier.AvailabilityChanged += OnAvailabilityChanged;
        try
        {
            Task first = Task.Run(FFmpegInstallNotifier.MarkMissing);
            Assert.That(firstNotificationEntered.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "the first availability notification did not start");

            Task second = Task.Run(FFmpegInstallNotifier.MarkInstalled);
            Assert.That(second.Wait(TimeSpan.FromMilliseconds(100)), Is.False,
                "an unrelated transition must wait for the active callback to finish");

            releaseFirstNotification.Set();
            Assert.That(first.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "the first transition did not complete after its callback was released");
            Assert.That(second.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "the queued transition did not complete after the first callback was released");
            Assert.That(observedStates.ToArray(), Is.EqualTo(new[] { true, false }),
                "each callback must receive the state snapshot for its queued transition");
        }
        finally
        {
            releaseFirstNotification.Set();
            FFmpegInstallNotifier.AvailabilityChanged -= OnAvailabilityChanged;
            FFmpegInstallNotifier.MarkInstalled();
        }
    }

    [Test]
    public void NotifyWorkerStarted_ClearsMissingLatchAndSignalsAvailability()
    {
        FFmpegInstallNotifier.MarkMissing();
        int changes = 0;
        void OnAvailabilityChanged(object? sender, EventArgs e) => changes++;

        FFmpegInstallNotifier.AvailabilityChanged += OnAvailabilityChanged;
        try
        {
            FFmpegInstallNotifier.NotifyWorkerStarted();

            Assert.Multiple(() =>
            {
                Assert.That(FFmpegInstallNotifier.IsLibrariesMissing, Is.False);
                Assert.That(FFmpegInstallNotifier.MissingSinceTicks, Is.EqualTo(0));
                Assert.That(changes, Is.EqualTo(1));
                Assert.That(FFmpegInstallNotifier.ShouldSkipStartProbe(long.MaxValue), Is.False);
            });
        }
        finally
        {
            FFmpegInstallNotifier.AvailabilityChanged -= OnAvailabilityChanged;
        }
    }

    // Regression test for the TOCTOU race: when many threads observe the same
    // pre-throttle state simultaneously, exactly one must acquire the slot.
    // The pre-fix Read/Exchange split allows >=2 winners; the CAS-based fix
    // collapses that to 1 deterministically.
    [Test]
    public void TryAcquireNotifySlot_UnderConcurrency_OnlyOneWinner()
    {
        const int iterations = 25;
        const int threads = 64;

        for (int i = 0; i < iterations; i++)
        {
            FFmpegInstallNotifier.MarkInstalled();

            long now = 1_000_000L + i; // any non-zero value works
            int winners = 0;
            using var barrier = new Barrier(threads);
            var tasks = new Task[threads];
            for (int t = 0; t < threads; t++)
            {
                tasks[t] = Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    if (FFmpegInstallNotifier.TryAcquireNotifySlot(now))
                        Interlocked.Increment(ref winners);
                });
            }

            Task.WaitAll(tasks);
            Assert.That(winners, Is.EqualTo(1), $"iteration {i}: expected exactly one winner");
        }
    }
}
