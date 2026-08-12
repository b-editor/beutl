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
    public void MarkVerificationStarted_DropsQueuedAvailabilityNotifications()
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
            Task blocker = Task.Run(FFmpegInstallNotifier.MarkInstalled);
            Assert.That(firstNotificationEntered.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "the first availability notification did not start");

            Task missing = Task.Run(FFmpegInstallNotifier.MarkMissing);
            Assert.That(SpinWait.SpinUntil(
                () => FFmpegInstallNotifier.IsLibrariesMissing,
                TimeSpan.FromSeconds(5)), Is.True,
                "the missing transition did not update state");

            FFmpegInstallNotifier.MarkVerificationStarted();
            Assert.That(FFmpegInstallNotifier.IsLibrariesMissing, Is.False);

            releaseFirstNotification.Set();
            Assert.That(blocker.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(missing.Wait(TimeSpan.FromSeconds(5)), Is.True);
            blocker.GetAwaiter().GetResult();
            missing.GetAwaiter().GetResult();

            Assert.That(observedStates.ToArray(), Is.EqualTo(new[] { false }),
                "verification must invalidate a queued missing snapshot");
        }
        finally
        {
            releaseFirstNotification.Set();
            FFmpegInstallNotifier.AvailabilityChanged -= OnAvailabilityChanged;
            FFmpegInstallNotifier.MarkInstalled();
        }
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
    public void NotifyMissing_UnderConcurrency_DoesNotRearmActiveCooldown()
    {
        FFmpegInstallNotifier.MarkMissing();
        long since = FFmpegInstallNotifier.MissingSinceTicks;
        const int callers = 32;
        using var barrier = new Barrier(callers);
        var tasks = new Task[callers];

        for (int i = 0; i < callers; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                barrier.SignalAndWait();
                FFmpegInstallNotifier.NotifyMissing();
            });
        }

        Task.WaitAll(tasks);
        Assert.That(FFmpegInstallNotifier.MissingSinceTicks, Is.EqualTo(since));
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
    public void AvailabilityChanged_CallbackCanWaitForCrossThreadTransition()
    {
        FFmpegInstallNotifier.MarkInstalled();

        void OnAvailabilityChanged(object? sender, EventArgs e)
        {
            if (((FFmpegLibraryAvailabilityChangedEventArgs)e).IsLibrariesMissing)
            {
                Task descendant = Task.Run(FFmpegInstallNotifier.MarkInstalled);
                if (!descendant.Wait(TimeSpan.FromSeconds(1)))
                    throw new TimeoutException("a callback-dispatched transition must not wait for the active dispatcher");
            }
        }

        FFmpegInstallNotifier.AvailabilityChanged += OnAvailabilityChanged;
        try
        {
            Task transition = Task.Run(FFmpegInstallNotifier.MarkMissing);

            Assert.That(transition.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "the callback and its cross-thread transition must both complete");
            transition.GetAwaiter().GetResult();
            Assert.That(FFmpegInstallNotifier.IsLibrariesMissing, Is.False);
        }
        finally
        {
            FFmpegInstallNotifier.AvailabilityChanged -= OnAvailabilityChanged;
            FFmpegInstallNotifier.MarkInstalled();
        }
    }

    [Test]
    public void AvailabilityChanged_CallbackCanWaitForSuppressedFlowTransition()
    {
        FFmpegInstallNotifier.MarkInstalled();

        void OnAvailabilityChanged(object? sender, EventArgs e)
        {
            if (((FFmpegLibraryAvailabilityChangedEventArgs)e).IsLibrariesMissing)
            {
                Task descendant;
                using (ExecutionContext.SuppressFlow())
                    descendant = Task.Run(FFmpegInstallNotifier.MarkInstalled);

                if (!descendant.Wait(TimeSpan.FromSeconds(1)))
                    throw new TimeoutException("a suppressed-flow transition must not wait for the active dispatcher");
            }
        }

        FFmpegInstallNotifier.AvailabilityChanged += OnAvailabilityChanged;
        try
        {
            Task transition = Task.Run(FFmpegInstallNotifier.MarkMissing);

            Assert.That(transition.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "the callback and its suppressed-flow transition must both complete");
            transition.GetAwaiter().GetResult();
            Assert.That(FFmpegInstallNotifier.IsLibrariesMissing, Is.False);
        }
        finally
        {
            FFmpegInstallNotifier.AvailabilityChanged -= OnAvailabilityChanged;
            FFmpegInstallNotifier.MarkInstalled();
        }
    }

    [Test]
    public void AvailabilityChanged_ReentrantTransitionPreservesFifoOrder()
    {
        FFmpegInstallNotifier.MarkInstalled();
        using var firstNotificationEntered = new ManualResetEventSlim();
        using var allowReentrantTransition = new ManualResetEventSlim();
        var observedStates = new ConcurrentQueue<bool>();
        int missingNotifications = 0;

        void OnAvailabilityChanged(object? sender, EventArgs e)
        {
            bool isMissing = ((FFmpegLibraryAvailabilityChangedEventArgs)e).IsLibrariesMissing;
            observedStates.Enqueue(isMissing);
            if (isMissing && Interlocked.Increment(ref missingNotifications) == 1)
            {
                firstNotificationEntered.Set();
                if (!allowReentrantTransition.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("the reentrant transition was not released");

                FFmpegInstallNotifier.MarkMissing();
            }
        }

        FFmpegInstallNotifier.AvailabilityChanged += OnAvailabilityChanged;
        try
        {
            Task first = Task.Run(FFmpegInstallNotifier.MarkMissing);
            Assert.That(firstNotificationEntered.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "the first availability notification did not start");

            using var installedStarted = new ManualResetEventSlim();
            Task installed = Task.Run(() =>
            {
                installedStarted.Set();
                FFmpegInstallNotifier.MarkInstalled();
            });
            Assert.That(installedStarted.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "the installed transition did not start");
            Assert.That(installed.Wait(TimeSpan.FromSeconds(1)), Is.True,
                "a transition racing an active callback must enqueue without waiting");

            allowReentrantTransition.Set();
            Assert.That(first.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(installed.Wait(TimeSpan.FromSeconds(5)), Is.True);
            first.GetAwaiter().GetResult();
            installed.GetAwaiter().GetResult();

            Assert.That(observedStates.ToArray(), Is.EqualTo(new[] { true, false, true }),
                "reentrant notifications must remain behind already queued transitions");
        }
        finally
        {
            allowReentrantTransition.Set();
            FFmpegInstallNotifier.AvailabilityChanged -= OnAvailabilityChanged;
            FFmpegInstallNotifier.MarkInstalled();
        }
    }

    [Test]
    public void AvailabilityChanged_NestedSubscriberExceptionDoesNotFaultOwningTransition()
    {
        FFmpegInstallNotifier.MarkInstalled();
        var expected = new InvalidOperationException("nested availability callback failed");

        void QueueInstalledTransition(object? sender, EventArgs e)
        {
            if (((FFmpegLibraryAvailabilityChangedEventArgs)e).IsLibrariesMissing)
                FFmpegInstallNotifier.MarkInstalled();
        }

        void ThrowForInstalledTransition(object? sender, EventArgs e)
        {
            if (!((FFmpegLibraryAvailabilityChangedEventArgs)e).IsLibrariesMissing)
                throw expected;
        }

        FFmpegInstallNotifier.AvailabilityChanged += QueueInstalledTransition;
        FFmpegInstallNotifier.AvailabilityChanged += ThrowForInstalledTransition;
        try
        {
            Task transition = Task.Run(FFmpegInstallNotifier.MarkMissing);

            Assert.That(transition.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "the owning transition must not receive a nested transition's exception");
            transition.GetAwaiter().GetResult();
            Assert.That(FFmpegInstallNotifier.IsLibrariesMissing, Is.False);
        }
        finally
        {
            FFmpegInstallNotifier.AvailabilityChanged -= QueueInstalledTransition;
            FFmpegInstallNotifier.AvailabilityChanged -= ThrowForInstalledTransition;
            FFmpegInstallNotifier.MarkInstalled();
        }
    }

    [Test]
    public void AvailabilityChanged_ConcurrentTransitionDoesNotBlockOnForeignCallbackException()
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
            Assert.That(second.Wait(TimeSpan.FromSeconds(1)), Is.True,
                "a transition racing an active callback must not wait for its notification");

            releaseFirstNotification.Set();
            Assert.That(first.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "the dispatcher must not receive another caller's subscriber exception");
            first.GetAwaiter().GetResult();
            second.GetAwaiter().GetResult();
            Assert.That(second.IsFaulted, Is.False,
                "a foreign subscriber exception must remain isolated from a non-blocking transition");
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
            Assert.That(second.Wait(TimeSpan.FromSeconds(1)), Is.True,
                "an unrelated transition must enqueue without waiting for the active callback");

            releaseFirstNotification.Set();
            Assert.That(first.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "the first transition did not complete after its callback was released");
            Assert.That(second.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "the queued transition did not complete after the first callback was released");
            second.GetAwaiter().GetResult();
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
