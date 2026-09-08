using System.Diagnostics;
using Beutl.PackageTools.UI.Services;

namespace Beutl.UnitTests.PackageTools;

[TestFixture]
public sealed class AsyncOperationLifetimeTests
{
    [Test]
    public async Task DisposeAsync_CancelsAndDrainsOperationsBeforeDisposingResources()
    {
        var operationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowOperationToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestsCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool resourcesDisposed = false;
        var lifetime = new AsyncOperationLifetime(
            () => requestsCanceled.TrySetResult(),
            () =>
            {
                resourcesDisposed = true;
                return ValueTask.CompletedTask;
            });
        Task operation = lifetime.RunAsync(async cancellationToken =>
        {
            operationStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancellationObserved.TrySetResult();
            }

            await allowOperationToFinish.Task;
        });
        await operationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task dispose = lifetime.DisposeAsync().AsTask();
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(requestsCanceled.Task.IsCompleted, Is.True);
            Assert.That(resourcesDisposed, Is.False);
            Assert.That(dispose.IsCompleted, Is.False);
        });

        allowOperationToFinish.TrySetResult();
        await Task.WhenAll(operation, dispose).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(resourcesDisposed, Is.True);
    }

    [Test]
    public async Task DisposeAsync_IsIdempotentAndRejectsNewOperations()
    {
        int disposeCount = 0;
        var lifetime = new AsyncOperationLifetime(
            static () => { },
            () =>
            {
                disposeCount++;
                return ValueTask.CompletedTask;
            });

        Task first = lifetime.DisposeAsync().AsTask();
        Task second = lifetime.DisposeAsync().AsTask();
        await Task.WhenAll(first, second);

        Assert.Multiple(() =>
        {
            Assert.That(disposeCount, Is.EqualTo(1));
            Assert.That(
                () => lifetime.RunAsync(static _ => Task.CompletedTask),
                Throws.TypeOf<ObjectDisposedException>());
        });
    }

    [Test]
    public async Task DisposeAsync_StillDrainsAndDisposesWhenCancellationThrows()
    {
        var operationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowOperationToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool resourcesDisposed = false;
        var lifetime = new AsyncOperationLifetime(
            static () => throw new InvalidOperationException("cancel failed"),
            () =>
            {
                resourcesDisposed = true;
                return ValueTask.CompletedTask;
            });
        Task operation = lifetime.RunAsync(async cancellationToken =>
        {
            operationStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancellationObserved.TrySetResult();
            }

            await allowOperationToFinish.Task;
        });
        await operationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task dispose = lifetime.DisposeAsync().AsTask();
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        allowOperationToFinish.TrySetResult();

        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await dispose.WaitAsync(TimeSpan.FromSeconds(5)),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(resourcesDisposed, Is.True);
        });
    }

    [Test]
    public async Task RunAsync_InvokesCompletionWhenCallerCancelsButLifetimeIsNotStopping()
    {
        var operationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowOperationToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int completionCount = 0;
        var lifetime = new AsyncOperationLifetime(
            static () => { },
            static () => ValueTask.CompletedTask);
        using var callerCancellation = new CancellationTokenSource();

        Task operation = lifetime.RunAsync(
            async cancellationToken =>
            {
                operationStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    cancellationObserved.TrySetResult();
                }

                await allowOperationToFinish.Task;
            },
            () => Interlocked.Increment(ref completionCount),
            callerCancellation.Token);
        await operationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        callerCancellation.Cancel();
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        allowOperationToFinish.TrySetResult();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(completionCount, Is.EqualTo(1));
    }

    [Test]
    public async Task DisposeAsync_RunsCancelPendingRequests_WhenTokenCallbackThrows()
    {
        var operationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowOperationToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool requestsCanceled = false;
        var lifetime = new AsyncOperationLifetime(
            () => requestsCanceled = true,
            static () => ValueTask.CompletedTask);

        Task operation = lifetime.RunAsync(async cancellationToken =>
        {
            operationStarted.TrySetResult();
            using var registration = cancellationToken.Register(static () =>
                throw new InvalidOperationException("callback failed"));
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }

            await allowOperationToFinish.Task;
        });
        await operationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task dispose = lifetime.DisposeAsync().AsTask();
        allowOperationToFinish.TrySetResult();

        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.CatchAsync<Exception>(async () =>
            await dispose.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.That(requestsCanceled, Is.True,
            "CancelPendingRequests must run even when a token callback throws");
    }

    [Test]
    public async Task DisposeAsync_StopsWaitingAtTheDrainDeadline_WhenAnOperationNeverCompletes()
    {
        var operationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool resourcesDisposed = false;
        var lifetime = new AsyncOperationLifetime(
            static () => { },
            () =>
            {
                resourcesDisposed = true;
                return ValueTask.CompletedTask;
            },
            drainDeadlineMilliseconds: 500);
        Task operation = lifetime.RunAsync(async cancellationToken =>
        {
            operationStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        });
        await operationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var stopwatch = Stopwatch.StartNew();
        await lifetime.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        stopwatch.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)),
                "disposal must stop waiting at the drain deadline even when an operation never completes");
            Assert.That(resourcesDisposed, Is.True,
                "resources must still be disposed after the drain deadline expires");
        });
    }

}
