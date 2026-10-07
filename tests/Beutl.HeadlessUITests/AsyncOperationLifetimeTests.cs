using System.Diagnostics;
using Beutl.Services;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AsyncOperationLifetimeTests
{
    [Test]
    public async Task Cancel_EndsOneOperationAndLeavesTheRestRunning()
    {
        await using var lifetime = new AsyncOperationLifetime();
        using AsyncOperationLifetime.Operation abandoned = lifetime.TryEnter()!;
        using AsyncOperationLifetime.Operation kept = lifetime.TryEnter()!;

        abandoned.Cancel();
        using AsyncOperationLifetime.Operation? next = lifetime.TryEnter();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(abandoned.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(kept.CancellationToken.IsCancellationRequested, Is.False,
                "Leaving one long request must not shut down everything else the tab is doing.");
            Assert.That(next, Is.Not.Null,
                "And the tab still admits the next request.");
        }
    }

    [Test]
    public async Task IdentitySwitchRejectsLatePublicationAndCancelsItsToken()
    {
        await using var lifetime = new AsyncOperationLifetime();
        using var identity = new IdentityOperationLifetime();
        using IdentityOperationLifetime.Operation operation = identity.TryEnter(lifetime)!;

        bool cleared = false;
        identity.Switch(() => cleared = true);

        Assert.Multiple(() =>
        {
            Assert.That(cleared, Is.True);
            Assert.That(operation.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(operation.TryPublish(static () => { }), Is.False);
        });
    }

    [Test]
    public async Task DeferredIdentitySwitchFencesOldWorkBeforeUiClearRuns()
    {
        await using var lifetime = new AsyncOperationLifetime();
        using var identity = new IdentityOperationLifetime();
        using IdentityOperationLifetime.Operation oldOperation = identity.TryEnter(lifetime)!;
        Action? scheduledClear = null;
        bool cleared = false;

        identity.SwitchDeferred(action => scheduledClear = action, () => cleared = true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(oldOperation.TryPublish(static () => { }), Is.False);
            Assert.That(identity.TryEnter(lifetime), Is.Null);
            Assert.That(cleared, Is.False);
        }

        scheduledClear!();
        using IdentityOperationLifetime.Operation? newOperation = identity.TryEnter(lifetime);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cleared, Is.True);
            Assert.That(newOperation, Is.Not.Null);
        }
    }

    [Test]
    public void DeferredIdentitySwitchSkipsQueuedUiClearAfterDisposal()
    {
        var identity = new IdentityOperationLifetime();
        Action? scheduledClear = null;
        bool cleared = false;
        identity.SwitchDeferred(action => scheduledClear = action, () => cleared = true);

        identity.Dispose();

        Assert.DoesNotThrow(() => scheduledClear!());
        Assert.That(cleared, Is.False);
    }

    [Test]
    public async Task IdentitySwitchWaitsForPublicationAdmittedBeforeTheSwitch()
    {
        await using var lifetime = new AsyncOperationLifetime();
        using var identity = new IdentityOperationLifetime();
        using IdentityOperationLifetime.Operation operation = identity.TryEnter(lifetime)!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<bool> publication = Task.Run(() => operation.TryPublish(() =>
        {
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
        }));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task switched = Task.Run(() => identity.Switch(static () => { }));
        await Task.Delay(50);
        Assert.That(switched.IsCompleted, Is.False);
        release.TrySetResult();

        Assert.That(await publication.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
        await switched.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(operation.TryPublish(static () => { }), Is.False);
    }

    [Test]
    public async Task IdentitySwitchClearsWhenCancellationCallbackThrows()
    {
        await using var lifetime = new AsyncOperationLifetime();
        using var identity = new IdentityOperationLifetime();
        using IdentityOperationLifetime.Operation operation = identity.TryEnter(lifetime)!;
        using CancellationTokenRegistration registration = operation.CancellationToken.Register(
            static () => throw new InvalidOperationException("ignored"));

        bool cleared = false;
        identity.Switch(() => cleared = true);

        Assert.That(cleared, Is.True);
    }

    [Test]
    public async Task IdentitySwitchDoesNotWaitForBlockingCancellationCallback()
    {
        await using var lifetime = new AsyncOperationLifetime();
        using var identity = new IdentityOperationLifetime();
        using IdentityOperationLifetime.Operation operation = identity.TryEnter(lifetime)!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration = operation.CancellationToken.Register(() =>
        {
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
        });

        bool cleared = false;
        Task switchTask = Task.Run(() => identity.Switch(() => cleared = true));
        await switchTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(cleared, Is.True);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        release.TrySetResult();
    }

    [Test]
    public async Task Cancel_StillPublishesSoTheViewModelCanResetItsState()
    {
        await using var lifetime = new AsyncOperationLifetime();
        using AsyncOperationLifetime.Operation operation = lifetime.TryEnter()!;

        operation.Cancel();

        Assert.That(operation.TryPublish(() => { }), Is.True,
            "Cancelling is not shutdown, so the finally block can still clear the running flag.");
    }

    [Test]
    public async Task ConcurrentPublicationsForOneOperationAreSerialized()
    {
        await using var lifetime = new AsyncOperationLifetime();
        using AsyncOperationLifetime.Operation operation = lifetime.TryEnter()!;
        int active = 0;
        int maximum = 0;
        Task<bool>[] publications = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            operation.TryPublish(() =>
            {
                int current = Interlocked.Increment(ref active);
                int observed;
                do
                {
                    observed = Volatile.Read(ref maximum);
                    if (observed >= current)
                        break;
                }
                while (Interlocked.CompareExchange(ref maximum, current, observed) != observed);
                Thread.Sleep(10);
                Interlocked.Decrement(ref active);
            }))).ToArray();

        Assert.That(await Task.WhenAll(publications), Has.All.True);
        Assert.That(maximum, Is.EqualTo(1));
    }

    [Test]
    public async Task Dispose_EndsEveryOperationAdmittedSoFar()
    {
        var lifetime = new AsyncOperationLifetime();
        AsyncOperationLifetime.Operation first = lifetime.TryEnter()!;
        AsyncOperationLifetime.Operation second = lifetime.TryEnter()!;

        // Disposal waits for what it cancelled, so the operations are released first.
        Task disposal = lifetime.DisposeAsync().AsTask();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => first.CancellationToken.IsCancellationRequested, Is.True.After(5000, 10));
            Assert.That(() => second.CancellationToken.IsCancellationRequested, Is.True.After(5000, 10));
            Assert.That(lifetime.TryEnter(), Is.Null);
            Assert.That(first.TryPublish(() => { }), Is.False);
        }

        first.Dispose();
        second.Dispose();
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Dispose_DoesNotBlockBeforeTheSharedDeadlineWhenPublicationIsInProgress()
    {
        var lifetime = new AsyncOperationLifetime();
        AsyncOperationLifetime.Operation operation = lifetime.TryEnter()!;
        var publicationEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePublication = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Task<bool> publication = Task.Run(() => operation.TryPublish(() =>
        {
            publicationEntered.TrySetResult();
            releasePublication.Task.GetAwaiter().GetResult();
        }));
        await publicationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task<Task> disposeInvocation = Task.Factory.StartNew(
            () => lifetime.DisposeAsync().AsTask(),
            CancellationToken.None,
            TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default);
        Task disposal = await disposeInvocation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(disposal.IsCompleted, Is.False);

        // Releasing the operation handle must not wait on callback code. The
        // admitted publication remains counted in the lifetime drain.
        Task dispose = Task.Run(operation.Dispose);
        await dispose.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(disposal.IsCompleted, Is.False);
        releasePublication.TrySetResult();
        Assert.That(await publication.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Dispose_RejectsPublicationAfterShutdownAdmission()
    {
        var lifetime = new AsyncOperationLifetime();
        using AsyncOperationLifetime.Operation operation = lifetime.TryEnter()!;

        Task disposal = lifetime.DisposeAsync().AsTask();

        Assert.That(operation.TryPublish(static () => { }), Is.False);

        operation.Dispose();
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Dispose_DrainsReentrantPublicationWithoutHoldingTheGate()
    {
        var lifetime = new AsyncOperationLifetime(TimeSpan.FromMilliseconds(100));
        using AsyncOperationLifetime.Operation operation = lifetime.TryEnter()!;
        var callbackReturned = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Task<bool> publication = Task.Run(() => operation.TryPublish(() =>
        {
            // DisposeAsync must publish its task before waiting for this admitted callback,
            // otherwise a callback that re-enters shutdown would deadlock the lifetime gate.
            lifetime.DisposeAsync().AsTask().GetAwaiter().GetResult();
            callbackReturned.TrySetResult();
        }));

        Assert.That(await publication.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
        await callbackReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        operation.Dispose();
        await lifetime.DisposeAsync();
    }

    [Test]
    public async Task Dispose_DrainsPublicationWhenCallbackThrows()
    {
        var lifetime = new AsyncOperationLifetime(TimeSpan.FromSeconds(1));
        using AsyncOperationLifetime.Operation operation = lifetime.TryEnter()!;
        var callbackEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Task publication = Task.Run(() => operation.TryPublish(() =>
        {
            callbackEntered.TrySetResult();
            releaseCallback.Task.GetAwaiter().GetResult();
            throw new InvalidOperationException("publication failed");
        }));
        await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task disposal = lifetime.DisposeAsync().AsTask();
        operation.Dispose();
        releaseCallback.TrySetResult();

        Assert.That(
            async () => await publication.WaitAsync(TimeSpan.FromSeconds(5)),
            Throws.InstanceOf<InvalidOperationException>());
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Dispose_CompletesAtDeadlineAndContinuesDraining()
    {
        var lifetime = new AsyncOperationLifetime(TimeSpan.FromMilliseconds(100));
        AsyncOperationLifetime.Operation operation = lifetime.TryEnter()!;
        var resourcesDisposed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var stopwatch = Stopwatch.StartNew();
        Task disposal = lifetime.DisposeAsync(() =>
        {
            resourcesDisposed.TrySetResult();
            return ValueTask.CompletedTask;
        }).AsTask();
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        stopwatch.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
            Assert.That(disposal.IsCompletedSuccessfully, Is.True);
            Assert.That(resourcesDisposed.Task.IsCompleted, Is.False,
                "The deadline only releases the caller; the actual operation drain must continue.");
        });

        operation.Dispose();
        await resourcesDisposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public void Dispose_ReportsCancellationCallbackFailureAfterDrain()
    {
        var lifetime = new AsyncOperationLifetime();
        AsyncOperationLifetime.Operation operation = lifetime.TryEnter()!;
        using CancellationTokenRegistration registration = operation.CancellationToken.Register(
            static () => throw new InvalidOperationException("callback failed"));

        Task disposal = lifetime.DisposeAsync().AsTask();
        operation.Dispose();

        Assert.That(
            async () => await disposal.WaitAsync(TimeSpan.FromSeconds(5)),
            Throws.InstanceOf<InvalidOperationException>());
    }

    [Test]
    public async Task Cancel_AfterDisposeIsIgnored()
    {
        await using var lifetime = new AsyncOperationLifetime();
        AsyncOperationLifetime.Operation operation = lifetime.TryEnter()!;
        operation.Dispose();

        Assert.DoesNotThrow(operation.Cancel);
    }

    [Test]
    public void Cancel_ObservesCallbackFailureWithoutThrowingFromTheUiCommand()
    {
        var lifetime = new AsyncOperationLifetime();
        AsyncOperationLifetime.Operation operation = lifetime.TryEnter()!;
        bool healthyCallbackRan = false;
        using CancellationTokenRegistration healthy = operation.CancellationToken.Register(
            () => healthyCallbackRan = true);
        using CancellationTokenRegistration failing = operation.CancellationToken.Register(
            static () => throw new InvalidOperationException("callback failed"));

        Assert.DoesNotThrow(operation.Cancel);
        Assert.That(healthyCallbackRan, Is.True);

        operation.Dispose();
        Task disposal = lifetime.DisposeAsync().AsTask();
        Assert.That(
            async () => await disposal.WaitAsync(TimeSpan.FromSeconds(5)),
            Throws.InstanceOf<Exception>());
    }

    [Test]
    public async Task Dispose_PublishesTaskBeforeCancellationCallbacksCanReenter()
    {
        var callbackReturned = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var lifetime = new AsyncOperationLifetime(TimeSpan.FromMilliseconds(100));
        AsyncOperationLifetime.Operation operation = lifetime.TryEnter()!;
        using CancellationTokenRegistration registration = operation.CancellationToken.Register(() =>
        {
            lifetime.DisposeAsync().AsTask().GetAwaiter().GetResult();
            callbackReturned.TrySetResult();
        });

        var stopwatch = Stopwatch.StartNew();
        Task disposal = lifetime.DisposeAsync().AsTask();
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        stopwatch.Stop();
        await callbackReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        operation.Dispose();

        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
    }

    [Test]
    public async Task Dispose_ObservesCancellationCallbackFailureAndStillDisposesExactlyOnce()
    {
        var lifetime = new AsyncOperationLifetime();
        AsyncOperationLifetime.Operation operation = lifetime.TryEnter()!;
        int disposeCount = 0;
        using CancellationTokenRegistration registration = operation.CancellationToken.Register(
            static () => throw new InvalidOperationException("callback failed"));

        Task first = lifetime.DisposeAsync(() =>
        {
            Interlocked.Increment(ref disposeCount);
            return ValueTask.CompletedTask;
        }).AsTask();
        Task second = lifetime.DisposeAsync(() =>
        {
            Interlocked.Increment(ref disposeCount);
            return ValueTask.CompletedTask;
        }).AsTask();
        Assert.That(second, Is.SameAs(first));
        operation.Dispose();
        Assert.That(
            async () => await first.WaitAsync(TimeSpan.FromSeconds(5)),
            Throws.InstanceOf<Exception>());
        Assert.That(lifetime.TryEnter(), Is.Null);
        Assert.That(disposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Dispose_AdditionalCancellationUsesTheSharedDeadlineAndDefersCleanup()
    {
        var releaseCancellation = new ManualResetEventSlim();
        var resourcesDisposed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var lifetime = new AsyncOperationLifetime(TimeSpan.FromMilliseconds(100));

        var stopwatch = Stopwatch.StartNew();
        await lifetime.DisposeAsync(
            () => releaseCancellation.Wait(),
            () =>
            {
                resourcesDisposed.TrySetResult();
                return ValueTask.CompletedTask;
            });
        stopwatch.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
            Assert.That(resourcesDisposed.Task.IsCompleted, Is.False,
                "Resources must remain alive until a blocking cancellation callback returns.");
            Assert.That(lifetime.TryEnter(), Is.Null,
                "Disposal must close admission before a blocking cancellation callback starts.");
        });

        releaseCancellation.Set();
        await resourcesDisposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releaseCancellation.Dispose();
    }

    [Test]
    public void Dispose_AdditionalCancellationFailureStillDisposesResources()
    {
        int disposeCount = 0;
        var lifetime = new AsyncOperationLifetime();

        Task disposal = lifetime.DisposeAsync(
            static () => throw new InvalidOperationException("callback failed"),
            () =>
            {
                Interlocked.Increment(ref disposeCount);
                return ValueTask.CompletedTask;
            }).AsTask();

        Assert.That(
            async () => await disposal.WaitAsync(TimeSpan.FromSeconds(5)),
            Throws.InstanceOf<InvalidOperationException>());
        Assert.That(disposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Dispose_ReportsADeferredResourceFailureAfterTheDeadline()
    {
        var releaseCleanup = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var lifetime = new AsyncOperationLifetime(
            TimeSpan.FromMilliseconds(100),
            exception => observed.TrySetResult(exception));

        Task disposal = lifetime.DisposeAsync(async () =>
        {
            await releaseCleanup.Task;
            throw new InvalidOperationException("late cleanup failed");
        }).AsTask();
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(disposal.IsCompletedSuccessfully, Is.True);

        releaseCleanup.TrySetResult();
        Exception failure = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(failure, Is.TypeOf<InvalidOperationException>());
        Assert.That(failure.Message, Is.EqualTo("late cleanup failed"));
    }
}
