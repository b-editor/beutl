using Beutl.Threading;
using Microsoft.Extensions.Time.Testing;

namespace Beutl.UnitTests.Threading;

public class DispatcherTests
{
    [Test]
    public void Invoke()
    {
        int id = Environment.CurrentManagedThreadId;
        var dispatcher = Dispatcher.Spawn();

        int dispatcherId = dispatcher.Invoke(() => Environment.CurrentManagedThreadId);
        Assert.That(id, Is.Not.EqualTo(dispatcherId));

        dispatcher.Shutdown();
    }

    [Test]
    public async Task InvokeAsync()
    {
        int id = Environment.CurrentManagedThreadId;
        var dispatcher = Dispatcher.Spawn();

        int dispatcherId = await dispatcher.InvokeAsync(async () => await Task.FromResult(Environment.CurrentManagedThreadId));
        Assert.That(id, Is.Not.EqualTo(dispatcherId));

        dispatcher.Shutdown();
    }

    [Test]
    public void InvokeVoid_Cancel()
    {
        var dispatcher = Dispatcher.Spawn();

        Assert.Catch<OperationCanceledException>(
            () => dispatcher.Invoke(() => { }, ct: new(true)));

        dispatcher.Shutdown();
    }

    [Test]
    public void Invoke_Cancel()
    {
        var dispatcher = Dispatcher.Spawn();

        Assert.Catch<OperationCanceledException>(
            () => dispatcher.Invoke(() => 100, ct: new(true)));

        dispatcher.Shutdown();
    }

    [Test]
    public void InvokeSyncVoid_Cancel()
    {
        var dispatcher = Dispatcher.Spawn();

        Assert.CatchAsync<OperationCanceledException>(
            async () => await dispatcher.InvokeAsync(() => { }, ct: new(true)));

        dispatcher.Shutdown();
    }

    [Test]
    public void InvokeAsyncVoid_Cancel()
    {
        var dispatcher = Dispatcher.Spawn();

        Assert.CatchAsync<OperationCanceledException>(
            async () => await dispatcher.InvokeAsync(async () => await Task.Delay(100), ct: new(true)));

        dispatcher.Shutdown();
    }

    [Test]
    public void InvokeSync_Cancel()
    {
        var dispatcher = Dispatcher.Spawn();

        Assert.CatchAsync<OperationCanceledException>(
            async () => await dispatcher.InvokeAsync(() => 100, ct: new(true)));

        dispatcher.Shutdown();
    }

    [Test]
    public void InvokeAsync_Cancel()
    {
        var dispatcher = Dispatcher.Spawn();

        Assert.CatchAsync<OperationCanceledException>(
            async () => await dispatcher.InvokeAsync(async () => await Task.FromResult(100), ct: new(true)));

        dispatcher.Shutdown();
    }

    [Test]
    public void InvokeAsyncVoid_CancelWhileQueued_CancelsWithoutExecuting()
    {
        var dispatcher = Dispatcher.Spawn();
        using var blockerStarted = new ManualResetEventSlim();
        using var releaseBlocker = new ManualResetEventSlim();
        using var cts = new CancellationTokenSource();
        bool executed = false;

        try
        {
            dispatcher.Dispatch(() =>
            {
                blockerStarted.Set();
                releaseBlocker.Wait();
            }, DispatchPriority.High);
            Assert.That(blockerStarted.Wait(TimeSpan.FromSeconds(5)), Is.True, "The queue blocker did not start.");

            Task task = dispatcher.InvokeAsync(() => executed = true, ct: cts.Token);
            cts.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () => await task);
            Assert.That(executed, Is.False);
        }
        finally
        {
            releaseBlocker.Set();
            dispatcher.Shutdown();
        }
    }

    [Test]
    public void InvokeAsyncResult_CancelWhileQueued_CancelsWithoutExecuting()
    {
        var dispatcher = Dispatcher.Spawn();
        using var blockerStarted = new ManualResetEventSlim();
        using var releaseBlocker = new ManualResetEventSlim();
        using var cts = new CancellationTokenSource();
        bool executed = false;

        try
        {
            dispatcher.Dispatch(() =>
            {
                blockerStarted.Set();
                releaseBlocker.Wait();
            }, DispatchPriority.High);
            Assert.That(blockerStarted.Wait(TimeSpan.FromSeconds(5)), Is.True, "The queue blocker did not start.");

            Task<int> task = dispatcher.InvokeAsync(() =>
            {
                executed = true;
                return 42;
            }, ct: cts.Token);
            cts.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () => await task);
            Assert.That(executed, Is.False);
        }
        finally
        {
            releaseBlocker.Set();
            dispatcher.Shutdown();
        }
    }

    [Test]
    public void InvokeVoid_CancelAfterExecutionStarts_StopsWaiting()
    {
        var dispatcher = Dispatcher.Spawn();
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var operationFinished = new ManualResetEventSlim();
        using var cts = new CancellationTokenSource();

        try
        {
            Task invocation = Task.Run(() => dispatcher.Invoke(() =>
            {
                try
                {
                    started.Set();
                    release.Wait();
                }
                finally
                {
                    operationFinished.Set();
                }
            }, ct: cts.Token));

            Assert.That(started.Wait(TimeSpan.FromSeconds(5)), Is.True, "The dispatcher operation did not start.");
            cts.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () =>
                await invocation.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.That(operationFinished.IsSet, Is.False,
                "The synchronous caller must observe cancellation without waiting for the running delegate.");
            release.Set();
            Assert.That(operationFinished.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "The dispatcher operation did not finish after it was released.");
        }
        finally
        {
            release.Set();
            dispatcher.Shutdown();
        }
    }

    [Test]
    public void InvokeResult_CancelAfterExecutionStarts_StopsWaiting()
    {
        var dispatcher = Dispatcher.Spawn();
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var operationFinished = new ManualResetEventSlim();
        using var cts = new CancellationTokenSource();

        try
        {
            Task invocation = Task.Run(() => dispatcher.Invoke(() =>
            {
                try
                {
                    started.Set();
                    release.Wait();
                    return 42;
                }
                finally
                {
                    operationFinished.Set();
                }
            }, ct: cts.Token));

            Assert.That(started.Wait(TimeSpan.FromSeconds(5)), Is.True, "The dispatcher operation did not start.");
            cts.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () =>
                await invocation.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.That(operationFinished.IsSet, Is.False,
                "The synchronous result caller must observe cancellation without waiting for the running delegate.");
            release.Set();
            Assert.That(operationFinished.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "The dispatcher operation did not finish after it was released.");
        }
        finally
        {
            release.Set();
            dispatcher.Shutdown();
        }
    }

    [Test]
    public async Task InvokeAsyncVoid_CancelAfterExecutionStarts_DoesNotCancelCompletion()
    {
        var dispatcher = Dispatcher.Spawn();
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cts = new CancellationTokenSource();

        try
        {
            Task task = dispatcher.InvokeAsync(() =>
            {
                started.Set();
                release.Wait();
            }, ct: cts.Token);

            Assert.That(started.Wait(TimeSpan.FromSeconds(5)), Is.True, "The dispatcher operation did not start.");
            cts.Cancel();

            Assert.That(task.IsCompleted, Is.False,
                "Cancellation after execution starts must not complete the task before the operation finishes.");
            release.Set();
            await task;
        }
        finally
        {
            release.Set();
            dispatcher.Shutdown();
        }
    }

    [Test]
    public async Task InvokeAsyncResult_CancelAfterExecutionStarts_ReturnsResult()
    {
        var dispatcher = Dispatcher.Spawn();
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cts = new CancellationTokenSource();

        try
        {
            Task<int> task = dispatcher.InvokeAsync(() =>
            {
                started.Set();
                release.Wait();
                return 42;
            }, ct: cts.Token);

            Assert.That(started.Wait(TimeSpan.FromSeconds(5)), Is.True, "The dispatcher operation did not start.");
            cts.Cancel();

            Assert.That(task.IsCompleted, Is.False,
                "Cancellation after execution starts must not complete the task before the operation finishes.");
            release.Set();
            Assert.That(await task, Is.EqualTo(42));
        }
        finally
        {
            release.Set();
            dispatcher.Shutdown();
        }
    }

    [Test]
    public void InvokeExecutionContext()
    {
        var dispatcher = Dispatcher.Spawn();

        using (ExecutionContext.SuppressFlow())
        {
            dispatcher.Invoke(() => { });
        }

        dispatcher.Shutdown();
    }

    [Test]
    public async Task Yield()
    {
        var dispatcher = Dispatcher.Spawn();
        var tcs = new TaskCompletionSource();

        bool yielded = false;
        var task2 = dispatcher.InvokeAsync(async () =>
        {
            await tcs.Task;
            await Dispatcher.Yield();
            Assert.That(yielded, Is.True, "Dispatcher did not yield control as expected.");
        });
        var task1 = dispatcher.InvokeAsync(() => yielded = true);
        tcs.SetResult();

        await Task.WhenAll(task1, task2);

        dispatcher.Shutdown();
    }

    [Test]
    public void Yield_OnCompleted()
    {
        var dispatcher = Dispatcher.Spawn();
        dispatcher.Invoke(() =>
        {
            var task = Dispatcher.Yield();
            var awaiter = task.GetAwaiter();

            awaiter.OnCompleted(() => { });
        });
        dispatcher.Shutdown();
    }

    [Test]
    public void YieldOutsideDispatcher()
    {
        Assert.CatchAsync<DispatcherException>(async () =>
        {
            await Dispatcher.Yield();
            Assert.Pass();
        });
    }

    [Test]
    public void YieldOutsideDispatcher_Completed_Throws()
    {
        Assert.Catch(() =>
        {
            var task = Dispatcher.Yield();
            var awaiter = task.GetAwaiter();
            // if (!awaiter.IsCompleted) throws DispatcherException
            {
                awaiter.OnCompleted(() => Assert.Fail("Should not be called"));
            }
        });
    }

    [Test]
    public void UnhandledException_Handled()
    {
        var dispatcher = Dispatcher.Spawn();
        var exception = new Exception("Test");

        dispatcher.UnhandledException += (_, args) =>
        {
            Assert.That(args.Exception, Is.EqualTo(exception));
            args.Handled = true;
        };

        dispatcher.Dispatch(() => throw exception);

        dispatcher.Shutdown();
    }

    [Test]
    public void UnhandledException_Unhandled()
    {
        var dispatcher = Dispatcher.Spawn();
        var exception = new Exception("Test");

        dispatcher.UnhandledException += (_, args) =>
        {
            Assert.That(args.Exception, Is.EqualTo(exception));
            args.Handled = false;
        };

        Assert.Catch<Exception>(() => dispatcher.Invoke(() => throw exception));

        dispatcher.Shutdown();
    }

    [Test]
    public async Task Schedule()
    {
        var dispatcher = Dispatcher.Spawn();
        var scheduledAt = DateTime.UtcNow;
        var tcs = new TaskCompletionSource<DateTime>();

        dispatcher.Schedule(TimeSpan.FromMilliseconds(100), () => { tcs.SetResult(DateTime.UtcNow); });

        var responseAt = await tcs.Task;

        Assert.That(responseAt - scheduledAt, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(100)));

        dispatcher.Shutdown();
    }

    [Test]
    public async Task ScheduleAsync()
    {
        var dispatcher = Dispatcher.Spawn();
        var scheduledAt = DateTime.UtcNow;
        var tcs = new TaskCompletionSource<DateTime>();

        dispatcher.Schedule(TimeSpan.FromMilliseconds(100), async () =>
        {
            await Task.CompletedTask;
            tcs.SetResult(DateTime.UtcNow);
        });

        var responseAt = await tcs.Task;

        Assert.That(responseAt - scheduledAt, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(100)));

        dispatcher.Shutdown();
    }

    [Test]
    public async Task Schedule_Multiple()
    {
        var timeProvider = new FakeTimeProvider();
        var dispatcher = Dispatcher.Spawn(timeProvider);
        var scheduledAt = timeProvider.GetUtcNow();
        var tcs1 = new TaskCompletionSource<DateTime>();
        var tcs2 = new TaskCompletionSource<DateTime>();
        var delay = TimeSpan.FromMilliseconds(100);

        dispatcher.Schedule(delay, () => { tcs1.SetResult(DateTime.UtcNow); });
        dispatcher.Schedule(delay, () => { tcs2.SetResult(DateTime.UtcNow); });

        timeProvider.Advance(delay);
        Assert.That(await tcs1.Task - scheduledAt, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(100)));
        Assert.That(await tcs2.Task - scheduledAt, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(100)));

        dispatcher.Shutdown();
    }

    [Test]
    public void SyncContext_Send()
    {
        var dispatcher = Dispatcher.Spawn();
        var context = dispatcher.Invoke(() => SynchronizationContext.Current);
        Assert.That(context, Is.Not.Null, "SynchronizationContext.Current is null");

        bool value = false;
        context!.Send(_ =>
        {
            Thread.Sleep(10);
            value = true;
        }, null);

        Assert.That(value, Is.True, "Send did not execute the action synchronously");

        dispatcher.Shutdown();
    }

    [Test]
    public void SyncContext_Send_Throws()
    {
        var dispatcher = Dispatcher.Spawn();
        var context = dispatcher.Invoke(() => SynchronizationContext.Current);
        Assert.That(context, Is.Not.Null, "SynchronizationContext.Current is null");
        var exception = new Exception();

        Assert.Catch<Exception>(() => context!.Send(_ => throw exception, null));

        dispatcher.Shutdown();
    }

    [Test]
    public void SyncContext_Post()
    {
        var dispatcher = Dispatcher.Spawn();
        var context = dispatcher.Invoke(() => SynchronizationContext.Current);
        Assert.That(context, Is.Not.Null, "SynchronizationContext.Current is null");

        bool value = false;
        context!.Post(_ =>
        {
            Thread.Sleep(10);
            value = true;
        }, null);

        Assert.That(value, Is.False, "Post executed the action synchronously");

        dispatcher.Shutdown();
    }

    [Test]
    public async Task SyncContext_Post_Throws_Handled()
    {
        var dispatcher = Dispatcher.Spawn();
        var context = dispatcher.Invoke(() => SynchronizationContext.Current);
        Assert.That(context, Is.Not.Null, "SynchronizationContext.Current is null");
        var exception = new Exception();
        var tcs = new TaskCompletionSource();
        dispatcher.UnhandledException += (_, args) =>
        {
            Assert.That(args.Exception, Is.EqualTo(exception));
            args.Handled = true;
            tcs.SetResult();
        };

        context!.Post(_ => throw exception, null);

        await tcs.Task;
        dispatcher.Shutdown();
    }

    [Test]
    public async Task SyncContext_Post_Throws_Unhandled()
    {
        var dispatcher = Dispatcher.Spawn();
        dispatcher._catchExceptions = true;

        var context = dispatcher.Invoke(() => SynchronizationContext.Current);
        Assert.That(context, Is.Not.Null, "SynchronizationContext.Current is null");
        var exception = new Exception();
        var tcs = new TaskCompletionSource();
        dispatcher.UnhandledException += (_, args) =>
        {
            Assert.That(args.Exception, Is.EqualTo(exception));
            args.Handled = false;
            tcs.SetResult();
        };

        context!.Post(_ => throw exception, null);

        await tcs.Task;
        dispatcher.Shutdown();
    }

    [Test]
    public void HasShutdownStarted_Be_True_When_ShutdownStarted()
    {
        var dispatcher = Dispatcher.Spawn();
        Assert.That(dispatcher.HasShutdownStarted, Is.False);
        dispatcher.ShutdownStarted += (_, _) =>
        {
            Assert.That(dispatcher.HasShutdownStarted, Is.True);
        };

        dispatcher.Shutdown();
    }

    [Test]
    public void HasShutdownFinished_Be_True_When_ShutdownFinished()
    {
        var dispatcher = Dispatcher.Spawn();
        Assert.That(dispatcher.HasShutdownFinished, Is.False);
        dispatcher.ShutdownFinished += (_, _) =>
        {
            Assert.That(dispatcher.HasShutdownFinished, Is.True);
        };

        dispatcher.Shutdown();
    }

    [Test]
    public void VerifyAccess_Throws_OutsideDispatcherThread()
    {
        var dispatcher = Dispatcher.Spawn();
        Assert.Catch<InvalidOperationException>(() => dispatcher.VerifyAccess());
        dispatcher.Shutdown();
    }

    [Test]
    public void VerifyAccess_DoesNot_Throws_InsideDispatcherThread()
    {
        var dispatcher = Dispatcher.Spawn();
        dispatcher.Invoke(() => Assert.DoesNotThrow(() => dispatcher.VerifyAccess()));

        dispatcher.Shutdown();
    }

    [Test]
    public void Spawn_With_InitialOperation()
    {
        bool flag = false;
        var dispatcher = Dispatcher.Spawn(() => flag = true);
        dispatcher.Invoke(() => Assert.That(flag, Is.True));
        dispatcher.Shutdown();
    }

    [Test]
    public void Invoke_Inside_DispatcherThread()
    {
        var dispatcher = Dispatcher.Spawn();
        dispatcher.Invoke(() =>
        {
            dispatcher.Invoke(() => Assert.That(dispatcher.CheckAccess(), Is.True));
            Assert.That(dispatcher.Invoke(() => dispatcher.CheckAccess()), Is.True);
        });

        dispatcher.Shutdown();
    }

    [Test]
    public void Run()
    {
        var dispatcher = Dispatcher.Spawn();
        dispatcher.Run(() =>
        {
            Assert.That(dispatcher.CheckAccess(), Is.True);
        });
        dispatcher.Shutdown();
    }

    [Test]
    public void RunAsync()
    {
        var dispatcher = Dispatcher.Spawn();
        dispatcher.Run(async () =>
        {
            Assert.That(dispatcher.CheckAccess(), Is.True);
            await Task.CompletedTask;
        });
        dispatcher.Shutdown();
    }

    [Test]
    public void Run_InsideDispatcherThread()
    {
        var dispatcher = Dispatcher.Spawn();
        dispatcher.Run(() =>
        {
            Assert.That(dispatcher.CheckAccess(), Is.True);
            dispatcher.Run(() =>
            {
                Assert.That(dispatcher.CheckAccess(), Is.True);
            });
        });
        dispatcher.Shutdown();
    }

    [Test]
    public void RunAsync_InsideDispatcherThread()
    {
        var dispatcher = Dispatcher.Spawn();
        dispatcher.Run(() =>
        {
            Assert.That(dispatcher.CheckAccess(), Is.True);
            dispatcher.Run(async () =>
            {
                Assert.That(dispatcher.CheckAccess(), Is.True);
                await Task.CompletedTask;
            });
        });
        dispatcher.Shutdown();
    }

    // Regression test for the race condition where an operation posted just before
    // WaitForPendingOperations acquires the lock would be missed, causing the
    // dispatcher thread to block unnecessarily.
    [Test]
    public async Task Post_BeforeWaitTokenCreated_OperationExecutedPromptly()
    {
        var dispatcher = Dispatcher.Spawn();

        // Run multiple iterations to increase the chance of hitting the race window
        // between ExecuteAvailableOperations returning and WaitForPendingOperations
        // acquiring the lock.
        for (int i = 0; i < 50; i++)
        {
            var operationExecuted = new TaskCompletionSource();

            // Invoke ensures the dispatcher thread processes the no-op and then
            // transitions back to: ExecuteAvailableOperations (empty) -> WaitForPendingOperations.
            dispatcher.Invoke(() => { });

            // Immediately dispatch from this (non-dispatcher) thread.
            // This targets the window where the queue has been drained but the wait token
            // has not yet been created. Without the early-return check inside the lock,
            // the dispatcher would block on WaitOne and never process this operation.
            dispatcher.Dispatch(() => operationExecuted.SetResult());

            var completed = await Task.WhenAny(operationExecuted.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.That(completed, Is.EqualTo(operationExecuted.Task),
                $"Iteration {i}: Dispatched operation was not executed promptly; dispatcher may have blocked in WaitForPendingOperations");
        }

        dispatcher.Shutdown();
    }

    // Regression for the deadlock where Shutdown() races WaitForPendingOperations' lock acquisition:
    // without the inner-lock _running guard the dispatcher arms a _waitToken nothing cancels and
    // blocks on WaitOne() forever. The loop widens the race window; each iteration needs a fresh
    // dispatcher because Shutdown is terminal.
    [Test]
    public void Shutdown_RacingWithWaitForPendingOperations_DoesNotDeadlock()
    {
        for (int i = 0; i < 100; i++)
        {
            var dispatcher = Dispatcher.Spawn();

            // Mark background so a thread leaked by a regressed race cannot keep the test process
            // alive after the assertion fails.
            dispatcher.Thread.IsBackground = true;

            // Drain the queue so the dispatcher reaches WaitForPendingOperations, then shut down from
            // this thread to race its lock acquisition.
            dispatcher.Invoke(() => { });
            dispatcher.Shutdown();

            // Join the thread rather than poll HasShutdownFinished: the thread exits only when the
            // dispatcher loop ends, the exact condition under test. A timeout here means deadlock.
            Assert.That(
                dispatcher.Thread.Join(TimeSpan.FromSeconds(5)),
                Is.True,
                $"Iteration {i}: dispatcher deadlocked in WaitForPendingOperations after Shutdown");
        }
    }

    [Test]
    public async Task Invoke_AfterShutdown_CompletesWithFailureInsteadOfBlocking()
    {
        var dispatcher = Dispatcher.Spawn();
        dispatcher.Shutdown();
        Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(5)), Is.True);

        Task invoke = Task.Run(() => dispatcher.Invoke(() => { }));
        Task completed = await Task.WhenAny(invoke, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Multiple(() =>
        {
            Assert.That(completed, Is.SameAs(invoke),
                "Invoke queued after shutdown must not wait forever for a dispatcher that has already exited.");
            Assert.That(invoke.Exception?.InnerException, Is.InstanceOf<ObjectDisposedException>());
        });
    }

    [Test]
    public async Task Invoke_AcceptedBeforeShutdown_IsFaultedWhenQueueIsDrained()
    {
        var dispatcher = Dispatcher.Spawn();
        dispatcher.Thread.IsBackground = true;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        dispatcher.Dispatch(() =>
        {
            entered.Set();
            release.Wait();
        }, DispatchPriority.High);
        Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);

        Task pending = dispatcher.InvokeAsync(() => { }, DispatchPriority.Low);
        dispatcher.Shutdown();
        Task completed = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(2)));
        release.Set();

        Assert.Multiple(() =>
        {
            Assert.That(completed, Is.SameAs(pending),
                "an accepted synchronous request must be faulted instead of orphaned during shutdown");
            Assert.That(pending.Exception?.InnerException, Is.InstanceOf<ObjectDisposedException>());
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(5)), Is.True);
        });
    }

    [Test]
    public void TryDispatch_AcceptedCleanup_RunsAbortFallbackWhenShutdownDrainsQueue()
    {
        var dispatcher = Dispatcher.Spawn();
        dispatcher.Thread.IsBackground = true;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        bool cleanupRan = false;
        Exception? abortReason = null;
        try
        {
            dispatcher.Dispatch(() =>
            {
                entered.Set();
                release.Wait();
            }, DispatchPriority.High);
            Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);

            bool accepted = dispatcher.TryDispatch(
                () => cleanupRan = true,
                ex => abortReason = ex,
                DispatchPriority.Low);
            Assert.That(accepted, Is.True, "the cleanup must first enter the dispatcher queue");

            dispatcher.Shutdown();

            Assert.Multiple(() =>
            {
                Assert.That(cleanupRan, Is.False, "shutdown drained the queued action before it could run");
                Assert.That(abortReason, Is.InstanceOf<ObjectDisposedException>(),
                    "accepted cleanup must receive a fallback callback when shutdown abandons it");
            });
        }
        finally
        {
            release.Set();
            if (!dispatcher.HasShutdownStarted)
                dispatcher.Shutdown();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(5)), Is.True);
        }
    }

    // Regression: a throwing abort fallback used to escape mid-sweep, so later abandoned operations were never
    // aborted and their cleanup fallbacks never ran.
    [Test]
    public void Shutdown_ThrowingAbortFallback_StillAbortsRemainingOperationsAndSurfacesFailure()
    {
        var dispatcher = Dispatcher.Spawn();
        dispatcher.Thread.IsBackground = true;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var abortFailure = new InvalidOperationException("abort fallback failed");
        Exception? secondAbortReason = null;
        try
        {
            dispatcher.Dispatch(() =>
            {
                entered.Set();
                release.Wait();
            }, DispatchPriority.High);
            Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);

            Assert.That(dispatcher.TryDispatch(
                static () => { }, _ => throw abortFailure, DispatchPriority.Low), Is.True);
            Assert.That(dispatcher.TryDispatch(
                static () => { }, ex => secondAbortReason = ex, DispatchPriority.Low), Is.True);

            InvalidOperationException? surfaced =
                Assert.Throws<InvalidOperationException>(dispatcher.Shutdown);

            Assert.Multiple(() =>
            {
                Assert.That(surfaced, Is.SameAs(abortFailure),
                    "the first abort-fallback failure surfaces after the sweep completes");
                Assert.That(secondAbortReason, Is.InstanceOf<ObjectDisposedException>(),
                    "a throwing abort fallback must not stop the remaining abort sweep");
                Assert.That(dispatcher.HasShutdownStarted, Is.True,
                    "shutdown still progresses when an abort fallback throws");
            });
        }
        finally
        {
            release.Set();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(5)), Is.True);
        }
    }

    // Regression: an unguarded negative `next - now` (timer slips past between flush and wait)
    // used to throw in CancelAfter and kill the dispatcher thread.
    [Test]
    public void Schedule_WhenTimerElapsesBetweenFlushAndWait_DoesNotCrashDispatcher()
    {
        // step < delay < 2*step: timer is future at the flush read (+10) but past at the wait read (+20).
        var timeProvider = new AdvancingTimeProvider(DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(10));
        var dispatcher = Dispatcher.Spawn(timeProvider);
        var tcs = new TaskCompletionSource();

        dispatcher.Schedule(TimeSpan.FromMilliseconds(15), () => tcs.SetResult());

        Assert.That(tcs.Task.Wait(TimeSpan.FromSeconds(5)), Is.True,
            "Scheduled operation did not run; the dispatcher likely crashed on CancelAfter with a negative delay.");

        dispatcher.Shutdown();
    }

    // Advances a fixed step per GetUtcNow() so a timer deterministically slips past between
    // the dispatcher's flush and wait clock reads.
    private sealed class AdvancingTimeProvider(DateTimeOffset start, TimeSpan step) : TimeProvider
    {
        private readonly object _lock = new();
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_lock)
            {
                DateTimeOffset now = _now;
                _now += step;
                return now;
            }
        }
    }
}
