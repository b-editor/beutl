using Beutl.Services;
using System.Diagnostics;

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
    public async Task Cancel_StillPublishesSoTheViewModelCanResetItsState()
    {
        await using var lifetime = new AsyncOperationLifetime();
        using AsyncOperationLifetime.Operation operation = lifetime.TryEnter()!;

        operation.Cancel();

        Assert.That(operation.TryPublish(() => { }), Is.True,
            "Cancelling is not shutdown, so the finally block can still clear the running flag.");
    }

    [Test]
    public async Task Stop_EndsEveryOperationAdmittedSoFar()
    {
        var lifetime = new AsyncOperationLifetime();
        AsyncOperationLifetime.Operation first = lifetime.TryEnter()!;
        AsyncOperationLifetime.Operation second = lifetime.TryEnter()!;

        // Stopping waits for what it cancelled, so the operations are released first.
        Task stopping = lifetime.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(second.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(lifetime.TryEnter(), Is.Null);
            Assert.That(first.TryPublish(() => { }), Is.False);
        }

        first.Dispose();
        second.Dispose();
        await stopping;
        await lifetime.DisposeAsync();
    }

    [Test]
    public async Task Stop_DoesNotBlockBeforeTheSharedDeadlineWhenPublicationIsInProgress()
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

        Task<Task> stopInvocation = Task.Factory.StartNew(
            lifetime.StopAsync,
            CancellationToken.None,
            TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default);
        Task stopping = await stopInvocation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(stopping.IsCompleted, Is.False);

        releasePublication.TrySetResult();
        Assert.That(await publication.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
        operation.Dispose();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        await lifetime.DisposeAsync();
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
}
