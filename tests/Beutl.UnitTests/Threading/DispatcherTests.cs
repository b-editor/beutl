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
    public void Invoke_WithTimeout_ReturnsTheResult()
    {
        var dispatcher = Dispatcher.Spawn();

        Assert.That(dispatcher.Invoke(() => 42, TimeSpan.FromMinutes(1)), Is.EqualTo(42));

        dispatcher.Shutdown();
    }

    [Test]
    public void Invoke_WithTimeout_DropsAnOperationStillQueuedAtTheDeadline()
    {
        var dispatcher = Dispatcher.Spawn();
        using var release = new ManualResetEventSlim();
        dispatcher.Dispatch(() => release.Wait(TimeSpan.FromMinutes(1)));
        bool ran = false;

        Assert.Throws<TimeoutException>(() => dispatcher.Invoke(() => { ran = true; }, TimeSpan.FromMilliseconds(50)));
        release.Set();
        dispatcher.Invoke(() => { });

        Assert.That(ran, Is.False);
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

    private static IEnumerable<TestCaseData> CanceledInvocations()
    {
        yield return Case("InvokeVoid", (d, ct) =>
        {
            d.Invoke(() => { }, ct: ct);
            return Task.CompletedTask;
        });
        yield return Case("Invoke", (d, ct) =>
        {
            d.Invoke(() => 100, ct: ct);
            return Task.CompletedTask;
        });
        yield return Case("InvokeSyncVoid", (d, ct) => d.InvokeAsync(() => { }, ct: ct));
        yield return Case("InvokeAsyncVoid", (d, ct) => d.InvokeAsync(async () => await Task.Delay(100), ct: ct));
        yield return Case("InvokeSync", (d, ct) => d.InvokeAsync(() => 100, ct: ct));
        yield return Case("InvokeAsync", (d, ct) => d.InvokeAsync(async () => await Task.FromResult(100), ct: ct));

        static TestCaseData Case(string name, Func<Dispatcher, CancellationToken, Task> invoke)
            => new TestCaseData(invoke).SetArgDisplayNames(name);
    }

    [TestCaseSource(nameof(CanceledInvocations))]
    public async Task Invoke_WithCanceledToken_Throws(Func<Dispatcher, CancellationToken, Task> invoke)
    {
        var dispatcher = Dispatcher.Spawn();

        await Assert.CatchAsync<OperationCanceledException>(() => invoke(dispatcher, new CancellationToken(true)));

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
        var tcs1 = new TaskCompletionSource();
        var tcs2 = new TaskCompletionSource();
        var delay = TimeSpan.FromMilliseconds(100);

        dispatcher.Schedule(delay, () => tcs1.SetResult());
        dispatcher.Schedule(delay, () => tcs2.SetResult());

        timeProvider.Advance(delay);
        await tcs1.Task;
        await tcs2.Task;

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

        dispatcher.Shutdown();

        Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(30)), Is.True);
        Assert.That(dispatcher.HasShutdownFinished, Is.True);
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

    // Regression: an operation dispatched after the queue drains but before the wait token exists was missed, and
    // the dispatcher blocked on WaitOne. Each iteration drains the queue and then dispatches into that window.
    [Test]
    public async Task Post_BeforeWaitTokenCreated_OperationExecutedPromptly()
    {
        var dispatcher = Dispatcher.Spawn();

        for (int i = 0; i < 50; i++)
        {
            var operationExecuted = new TaskCompletionSource();

            dispatcher.Invoke(() => { });

            dispatcher.Dispatch(() => operationExecuted.SetResult());

            var completed = await Task.WhenAny(operationExecuted.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.That(completed, Is.EqualTo(operationExecuted.Task),
                $"Iteration {i}: Dispatched operation was not executed promptly; dispatcher may have blocked in WaitForPendingOperations");
        }

        dispatcher.Shutdown();
    }

    // Regression: Shutdown() racing WaitForPendingOperations' lock left a _waitToken nothing cancels, and the
    // dispatcher blocked forever. Each iteration needs a fresh dispatcher because Shutdown is terminal.
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
