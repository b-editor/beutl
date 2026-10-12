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

        FFmpegLibraryState.AvailabilityChanged += OnAvailabilityChanged;
        try
        {
            FFmpegLibraryState.MarkMissing();
            FFmpegLibraryState.MarkMissing();
            FFmpegInstallNotifier.MarkInstalled();
        }
        finally
        {
            FFmpegLibraryState.AvailabilityChanged -= OnAvailabilityChanged;
        }

        Assert.That(changes, Is.EqualTo(2));
    }

    [Test]
    public void MarkVerificationStarted_ClearsMissingWithoutAvailabilitySignal()
    {
        FFmpegLibraryState.MarkMissing();
        int changes = 0;
        void OnAvailabilityChanged(object? sender, EventArgs e) => changes++;

        FFmpegLibraryState.AvailabilityChanged += OnAvailabilityChanged;
        try
        {
            FFmpegInstallNotifier.MarkVerificationStarted();
            Assert.Multiple(() =>
            {
                Assert.That(FFmpegLibraryState.IsLibrariesMissing, Is.False);
                Assert.That(FFmpegLibraryState.IsVerificationInProgress, Is.True);
                Assert.That(changes, Is.EqualTo(0));
            });

            FFmpegInstallNotifier.MarkInstalled();
        }
        finally
        {
            FFmpegLibraryState.AvailabilityChanged -= OnAvailabilityChanged;
        }

        Assert.That(changes, Is.EqualTo(1));
        Assert.That(FFmpegLibraryState.IsVerificationInProgress, Is.False);
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

        FFmpegLibraryState.AvailabilityChanged += OnAvailabilityChanged;
        try
        {
            Task blocker = Task.Run(FFmpegInstallNotifier.MarkInstalled);
            Assert.That(firstNotificationEntered.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "the first availability notification did not start");

            Task missing = Task.Run(FFmpegLibraryState.MarkMissing);
            Assert.That(SpinWait.SpinUntil(
                () => FFmpegLibraryState.IsLibrariesMissing,
                TimeSpan.FromSeconds(5)), Is.True,
                "the missing transition did not update state");

            FFmpegInstallNotifier.MarkVerificationStarted();
            Assert.That(FFmpegLibraryState.IsLibrariesMissing, Is.False);

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
            FFmpegLibraryState.AvailabilityChanged -= OnAvailabilityChanged;
            FFmpegInstallNotifier.MarkInstalled();
        }
    }

    [Test]
    public void ShouldSkipStartProbe_SkipsWithinCooldownThenAllowsAfterElapsed()
    {
        const long cooldownMs = 30_000; // mirrors FFmpegLibraryState.ReprobeCooldownMs

        FFmpegLibraryState.MarkMissing();
        long since = FFmpegLibraryState.MissingSinceTicks;

        Assert.Multiple(() =>
        {
            Assert.That(since, Is.Not.EqualTo(0), "MarkMissing must arm the re-probe cooldown");
            Assert.That(FFmpegLibraryState.ShouldSkipStartProbe(since + 1_000), Is.True);
            Assert.That(FFmpegLibraryState.ShouldSkipStartProbe(since + cooldownMs), Is.False);
            Assert.That(FFmpegLibraryState.ShouldSkipStartProbe(since + cooldownMs + 1_000), Is.False);
        });
    }

    [Test]
    public void ShouldSkipStartProbe_RepeatedProbesWithinCooldown_AllShortCircuit()
    {
        // A burst of decode attempts inside the cooldown window must all short-circuit (no worker
        // re-probe). This is what suppresses the hundreds-of-errors-per-session pattern: only the
        // first attempt in the window pays for a real worker start.
        FFmpegLibraryState.MarkMissing();
        long since = FFmpegLibraryState.MissingSinceTicks;

        for (long t = since; t < since + 25_000; t += 1_000)
        {
            Assert.That(FFmpegLibraryState.ShouldSkipStartProbe(t), Is.True,
                $"expected a short-circuit at t={t - since}ms into the cooldown");
        }
    }

    [Test]
    public void RecordMissingObserved_FirstObservation_ReturnsFalse()
    {
        FFmpegInstallNotifier.MarkInstalled();

        Assert.Multiple(() =>
        {
            Assert.That(FFmpegLibraryState.RecordMissingObserved(), Is.False,
                "the first observation is a genuine discovery, so callers must log it as an error");
            Assert.That(FFmpegLibraryState.IsLibrariesMissing, Is.True);
            Assert.That(FFmpegLibraryState.MissingSinceTicks, Is.EqualTo(0),
                "an observe-only decode failure must not arm the re-probe cooldown");
        });
    }

    [Test]
    public void RecordMissingObserved_SecondObservation_ReturnsTrue()
    {
        FFmpegInstallNotifier.MarkInstalled();
        FFmpegLibraryState.RecordMissingObserved();

        Assert.That(FFmpegLibraryState.RecordMissingObserved(), Is.True,
            "a later observation is an expected short-circuit, so callers must fail quietly");
    }

    [Test]
    public void RecordMissingObserved_DoesNotPushCooldownWindowForward()
    {
        FFmpegLibraryState.MarkMissing();
        long since = FFmpegLibraryState.MissingSinceTicks;

        FFmpegLibraryState.RecordMissingObserved();

        Assert.That(FFmpegLibraryState.MissingSinceTicks, Is.EqualTo(since),
            "a short-circuited decode must not re-arm and push the cooldown window forward");
    }

    [Test]
    public void NotifyMissing_DoesNotPushActiveCooldownWindowForward()
    {
        FFmpegLibraryState.MarkMissing();
        long since = FFmpegLibraryState.MissingSinceTicks;

        FFmpegInstallNotifier.NotifyMissing();

        Assert.That(FFmpegLibraryState.MissingSinceTicks, Is.EqualTo(since),
            "a short-circuited encoding retry must not re-arm the cooldown");
    }

    [Test]
    public void NotifyMissing_UnderConcurrency_DoesNotRearmActiveCooldown()
    {
        FFmpegLibraryState.MarkMissing();
        long since = FFmpegLibraryState.MissingSinceTicks;
        RunConcurrentIterations(32, 1,
            beforeIteration: () => { },
            action: FFmpegInstallNotifier.NotifyMissing,
            afterIteration: () => Assert.That(FFmpegLibraryState.MissingSinceTicks, Is.EqualTo(since)));
    }

    [Test]
    public void RecordMissingObserved_UnderConcurrency_OnlyOneFirstObservation()
    {
        int firstObservations = 0;
        RunConcurrentIterations(64, 25,
            beforeIteration: () =>
            {
                FFmpegInstallNotifier.MarkInstalled();
                firstObservations = 0;
            },
            action: () =>
            {
                if (!FFmpegLibraryState.RecordMissingObserved())
                    Interlocked.Increment(ref firstObservations);
            },
            afterIteration: () => Assert.That(firstObservations, Is.EqualTo(1)));
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

        FFmpegLibraryState.AvailabilityChanged += OnAvailabilityChanged;
        try
        {
            Task first = Task.Run(FFmpegLibraryState.MarkMissing);
            Assert.That(firstNotificationEntered.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "the first availability notification did not start");

            Assert.That(first.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "the state transition must complete while its subscriber is dispatching another transition");
            Assert.Multiple(() =>
            {
                Assert.That(FFmpegLibraryState.IsLibrariesMissing, Is.False);
                Assert.That(Volatile.Read(ref callbackCount), Is.EqualTo(2),
                    "both transitions must raise AvailabilityChanged");
                Assert.That(observedStates, Is.EqualTo(new[] { true, false }),
                    "reentrant transitions must be delivered in callback order");
            });
        }
        finally
        {
            FFmpegLibraryState.AvailabilityChanged -= OnAvailabilityChanged;
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

        FFmpegLibraryState.AvailabilityChanged += OnAvailabilityChanged;
        try
        {
            Task transition = Task.Run(FFmpegLibraryState.MarkMissing);

            Assert.That(transition.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "the callback and its cross-thread transition must both complete");
            transition.GetAwaiter().GetResult();
            Assert.That(FFmpegLibraryState.IsLibrariesMissing, Is.False);
        }
        finally
        {
            FFmpegLibraryState.AvailabilityChanged -= OnAvailabilityChanged;
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

        FFmpegLibraryState.AvailabilityChanged += OnAvailabilityChanged;
        try
        {
            Task transition = Task.Run(FFmpegLibraryState.MarkMissing);

            Assert.That(transition.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "the callback and its suppressed-flow transition must both complete");
            transition.GetAwaiter().GetResult();
            Assert.That(FFmpegLibraryState.IsLibrariesMissing, Is.False);
        }
        finally
        {
            FFmpegLibraryState.AvailabilityChanged -= OnAvailabilityChanged;
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

                FFmpegLibraryState.MarkMissing();
            }
        }

        FFmpegLibraryState.AvailabilityChanged += OnAvailabilityChanged;
        try
        {
            Task first = Task.Run(FFmpegLibraryState.MarkMissing);
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
            FFmpegLibraryState.AvailabilityChanged -= OnAvailabilityChanged;
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

        FFmpegLibraryState.AvailabilityChanged += QueueInstalledTransition;
        FFmpegLibraryState.AvailabilityChanged += ThrowForInstalledTransition;
        try
        {
            Task transition = Task.Run(FFmpegLibraryState.MarkMissing);

            Assert.That(transition.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "the owning transition must not receive a nested transition's exception");
            transition.GetAwaiter().GetResult();
            Assert.That(FFmpegLibraryState.IsLibrariesMissing, Is.False);
        }
        finally
        {
            FFmpegLibraryState.AvailabilityChanged -= QueueInstalledTransition;
            FFmpegLibraryState.AvailabilityChanged -= ThrowForInstalledTransition;
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

        FFmpegLibraryState.AvailabilityChanged += OnAvailabilityChanged;
        try
        {
            Task first = Task.Run(FFmpegLibraryState.MarkMissing);
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
            FFmpegLibraryState.AvailabilityChanged -= OnAvailabilityChanged;
            FFmpegInstallNotifier.MarkInstalled();
        }
    }

    [Test]
    public void AvailabilityChanged_PropagatesSubscriberException()
    {
        FFmpegInstallNotifier.MarkInstalled();
        var expected = new InvalidOperationException("availability callback failed");

        void OnAvailabilityChanged(object? sender, EventArgs e) => throw expected;

        FFmpegLibraryState.AvailabilityChanged += OnAvailabilityChanged;
        try
        {
            InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
                FFmpegLibraryState.MarkMissing);
            Assert.That(actual, Is.SameAs(expected));
        }
        finally
        {
            FFmpegLibraryState.AvailabilityChanged -= OnAvailabilityChanged;
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

        FFmpegLibraryState.AvailabilityChanged += OnAvailabilityChanged;
        try
        {
            Task first = Task.Run(FFmpegLibraryState.MarkMissing);
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
            FFmpegLibraryState.AvailabilityChanged -= OnAvailabilityChanged;
            FFmpegInstallNotifier.MarkInstalled();
        }
    }

    [Test]
    public void NotifyWorkerStarted_ClearsMissingLatchAndSignalsAvailability()
    {
        FFmpegLibraryState.MarkMissing();
        int changes = 0;
        void OnAvailabilityChanged(object? sender, EventArgs e) => changes++;

        FFmpegLibraryState.AvailabilityChanged += OnAvailabilityChanged;
        try
        {
            FFmpegLibraryState.NotifyWorkerStarted();

            Assert.Multiple(() =>
            {
                Assert.That(FFmpegLibraryState.IsLibrariesMissing, Is.False);
                Assert.That(FFmpegLibraryState.MissingSinceTicks, Is.EqualTo(0));
                Assert.That(changes, Is.EqualTo(1));
                Assert.That(FFmpegLibraryState.ShouldSkipStartProbe(long.MaxValue), Is.False);
            });
        }
        finally
        {
            FFmpegLibraryState.AvailabilityChanged -= OnAvailabilityChanged;
        }
    }

    // Regression test for the TOCTOU race: when many threads observe the same
    // pre-throttle state simultaneously, exactly one must acquire the slot.
    // The pre-fix Read/Exchange split allows >=2 winners; the CAS-based fix
    // collapses that to 1 deterministically.
    [Test]
    public void TryAcquireNotifySlot_UnderConcurrency_OnlyOneWinner()
    {
        long now = 1_000_000L;
        int winners = 0;
        RunConcurrentIterations(64, 25,
            beforeIteration: () =>
            {
                FFmpegInstallNotifier.MarkInstalled();
                now++;
                winners = 0;
            },
            action: () =>
            {
                if (FFmpegInstallNotifier.TryAcquireNotifySlot(now))
                    Interlocked.Increment(ref winners);
            },
            afterIteration: () => Assert.That(winners, Is.EqualTo(1)));
    }

    private static void RunConcurrentIterations(int participants, int iterations,
        Action beforeIteration, Action action, Action afterIteration)
    {
        // Dedicated workers avoid ThreadPool starvation at the barrier. Reuse them
        // across rounds instead of creating thousands of OS threads per test.
        using var barrier = new Barrier(participants, phase =>
        {
            if (phase.CurrentPhaseNumber % 2 == 0)
                beforeIteration();
            else
                afterIteration();
        });
        var tasks = new Task[participants];
        for (int i = 0; i < participants; i++)
        {
            tasks[i] = Task.Factory.StartNew(() =>
            {
                for (int round = 0; round < iterations; round++)
                {
                    if (!barrier.SignalAndWait(TimeSpan.FromSeconds(30)))
                        throw new TimeoutException("Concurrent test workers did not reach the start barrier.");
                    action();
                    if (!barrier.SignalAndWait(TimeSpan.FromSeconds(30)))
                        throw new TimeoutException("Concurrent test workers did not reach the end barrier.");
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        Task.WaitAll(tasks);
    }
}
