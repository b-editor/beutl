using System.Runtime.CompilerServices;
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
        SetCatchExceptions(dispatcher) = true;

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

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_catchExceptions")]
        static extern ref bool SetCatchExceptions(Dispatcher self);
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
}
