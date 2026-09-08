using Beutl.Graphics.Rendering;
using Beutl.Threading;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// Dispatcher.Shutdown() raises ShutdownStarted synchronously on whichever thread called it, and it does
// not interrupt the operation the dispatcher is already running: QueueSynchronizationContext only clears
// _running, which is re-read between operations. So HasShutdownStarted is not leave to run dispatcher-owned
// work off-thread - the owner thread may still be inside a frame that is reading the very resource the
// cleanup tears down. Only ShutdownFinished, raised once the loop has exited, says the thread is idle,
// which is why GpuResourceRelease waits for it too.
//
// The interleaving here is pinned rather than timed: the blocking operation parks on releaseOperation and
// only the test releases it, so the dispatcher thread provably cannot leave that operation before, during,
// or after the call under test. Any cleanup observed in that window overlaps a live operation.
public class DispatcherCleanupTests
{
    private const int TimeoutSeconds = 30;

    [Test]
    public void A_shutdown_that_only_started_leaves_the_cleanup_alone_until_the_thread_stops()
    {
        RunPinned((dispatcher, cleanup, probe) =>
        {
            cleanup.Request();
            Assert.That(probe.Calls, Is.Zero, "the cleanup is queued behind the operation being run");

            dispatcher.Shutdown();

            AssertNotRunBesideTheOperation(probe);
        });
    }

    [Test]
    public void A_request_made_during_a_started_shutdown_waits_for_the_thread_to_stop()
    {
        RunPinned((dispatcher, cleanup, probe) =>
        {
            dispatcher.Shutdown();

            // The inline route the shutdown state opens: Request() is the one deciding to run here.
            cleanup.Request();

            AssertNotRunBesideTheOperation(probe);
        });
    }

    [Test]
    public void An_abandoned_cleanup_is_not_run_by_a_finished_shutdown()
    {
        RunPinned(
            (dispatcher, cleanup, probe) =>
            {
                cleanup.Request();
                cleanup.Abandon();
                dispatcher.Shutdown();
                Assert.That(probe.Calls, Is.Zero);
            },
            expectedCallsAfterShutdown: 0);
    }

    [Test]
    public void A_dispatcher_still_draining_work_runs_the_cleanup_on_its_own_thread()
    {
        using var ran = new ManualResetEventSlim();
        Dispatcher dispatcher = Dispatcher.Spawn();
        int cleanupThreadId = 0;

        try
        {
            var cleanup = new DispatcherCleanup(
                dispatcher,
                () =>
                {
                    cleanupThreadId = Environment.CurrentManagedThreadId;
                    ran.Set();
                });

            cleanup.Request();

            Assert.That(ran.Wait(TimeSpan.FromSeconds(TimeoutSeconds)), Is.True, "the cleanup never ran");
            Assert.That(cleanupThreadId, Is.EqualTo(dispatcher.Thread.ManagedThreadId));
        }
        finally
        {
            Shutdown(dispatcher);
        }
    }

    [Test]
    public void A_request_after_the_shutdown_finished_runs_the_cleanup_at_once()
    {
        Dispatcher dispatcher = Dispatcher.Spawn();
        Shutdown(dispatcher);
        Assert.That(dispatcher.HasShutdownFinished, Is.True);

        var probe = new CleanupProbe(() => 0);
        var cleanup = new DispatcherCleanup(dispatcher, probe.Run);

        cleanup.Request();

        Assert.That(probe.Calls, Is.EqualTo(1), "nothing else will ever run a cleanup requested this late");
    }

    private static void AssertNotRunBesideTheOperation(CleanupProbe probe)
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                probe.CallsBesideALiveOperation, Is.Zero,
                "the cleanup ran beside the operation the dispatcher thread is still inside");
            Assert.That(
                probe.Calls, Is.Zero,
                "a shutdown that has only started must not run dispatcher-owned cleanup yet");
        });
    }

    /// <summary>
    /// Parks the dispatcher thread inside one operation, runs <paramref name="body"/> against a cleanup
    /// bound to that dispatcher, then lets the thread finish and asserts the cleanup settled exactly
    /// <paramref name="expectedCallsAfterShutdown"/> times.
    /// </summary>
    private static void RunPinned(
        Action<Dispatcher, DispatcherCleanup, CleanupProbe> body,
        int expectedCallsAfterShutdown = 1)
    {
        using var operationEntered = new ManualResetEventSlim();
        using var releaseOperation = new ManualResetEventSlim();
        Dispatcher dispatcher = Dispatcher.Spawn();
        int operationsRunning = 0;
        var probe = new CleanupProbe(() => Volatile.Read(ref operationsRunning));

        try
        {
            var cleanup = new DispatcherCleanup(dispatcher, probe.Run);

            dispatcher.Dispatch(
                () =>
                {
                    Interlocked.Increment(ref operationsRunning);
                    operationEntered.Set();
                    releaseOperation.Wait();
                    Interlocked.Decrement(ref operationsRunning);
                },
                DispatchPriority.High);
            Assert.That(
                operationEntered.Wait(TimeSpan.FromSeconds(TimeoutSeconds)), Is.True,
                "the dispatcher never entered the blocking operation");

            body(dispatcher, cleanup, probe);

            releaseOperation.Set();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(TimeoutSeconds)), Is.True);

            Assert.That(
                probe.Calls, Is.EqualTo(expectedCallsAfterShutdown),
                "the cleanup did not settle exactly once after the dispatcher thread stopped");
        }
        finally
        {
            releaseOperation.Set();
            Shutdown(dispatcher);
        }
    }

    private static void Shutdown(Dispatcher dispatcher)
    {
        if (!dispatcher.HasShutdownStarted)
            dispatcher.Shutdown();

        Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(TimeoutSeconds)), Is.True);
    }

    private sealed class CleanupProbe(Func<int> liveOperations)
    {
        private int _calls;
        private int _callsBesideALiveOperation;

        public int Calls => Volatile.Read(ref _calls);

        public int CallsBesideALiveOperation => Volatile.Read(ref _callsBesideALiveOperation);

        public void Run()
        {
            if (liveOperations() > 0)
                Interlocked.Increment(ref _callsBesideALiveOperation);

            Interlocked.Increment(ref _calls);
        }
    }
}
