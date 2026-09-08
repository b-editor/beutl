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
        bool requestsCanceled = false;
        int resourceDisposeCount = 0;
        var lifetime = new AsyncOperationLifetime(
            () => requestsCanceled = true,
            () =>
            {
                resourceDisposeCount++;
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
            Assert.That(requestsCanceled, Is.True);
            Assert.That(resourceDisposeCount, Is.Zero);
            Assert.That(dispose.IsCompleted, Is.False);
        });

        allowOperationToFinish.TrySetResult();
        await Task.WhenAll(operation, dispose).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(resourceDisposeCount, Is.EqualTo(1));
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
    public async Task DisposeAsync_PublishesOneTaskBeforeCancellationCanReenter()
    {
        var operationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? reentrantDisposal = null;
        int resourceDisposeCount = 0;
        AsyncOperationLifetime? lifetime = null;
        lifetime = new AsyncOperationLifetime(
            static () => { },
            () =>
            {
                Interlocked.Increment(ref resourceDisposeCount);
                return ValueTask.CompletedTask;
            });
        Task operation = lifetime.RunAsync(async cancellationToken =>
        {
            using CancellationTokenRegistration registration = cancellationToken.Register(() =>
                reentrantDisposal = lifetime.DisposeAsync().AsTask());
            operationStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        });
        await operationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task disposal = lifetime.DisposeAsync().AsTask();
        await Task.WhenAll(operation, disposal).WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reentrantDisposal, Is.SameAs(disposal));
            Assert.That(resourceDisposeCount, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task DisposeAsync_SynchronousReentrantWaitCompletesAtTheSharedDeadline()
    {
        var operationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int resourceDisposeCount = 0;
        AsyncOperationLifetime? lifetime = null;
        lifetime = new AsyncOperationLifetime(
            static () => { },
            () =>
            {
                resourceDisposeCount++;
                return ValueTask.CompletedTask;
            },
            TimeSpan.FromMilliseconds(100));
        Task operation = lifetime.RunAsync(async cancellationToken =>
        {
            using CancellationTokenRegistration registration = cancellationToken.Register(() =>
            {
                lifetime.DisposeAsync().AsTask().GetAwaiter().GetResult();
                callbackReturned.TrySetResult();
            });
            operationStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        });
        await operationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var stopwatch = Stopwatch.StartNew();
        await lifetime.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        stopwatch.Stop();
        await callbackReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => resourceDisposeCount == 1, TimeSpan.FromSeconds(5));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
            Assert.That(resourceDisposeCount, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task DisposeAsync_CancellationCallbackFailureCannotSkipOtherCancellationOrCleanup()
    {
        var operationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool pendingRequestsCanceled = false;
        int resourceDisposeCount = 0;
        var lifetime = new AsyncOperationLifetime(
            () => pendingRequestsCanceled = true,
            () =>
            {
                resourceDisposeCount++;
                return ValueTask.CompletedTask;
            });
        Task operation = lifetime.RunAsync(async cancellationToken =>
        {
            using CancellationTokenRegistration registration = cancellationToken.Register(static () =>
                throw new InvalidOperationException("callback failed"));
            operationStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        });
        await operationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.CatchAsync<Exception>(async () =>
            await lifetime.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.CatchAsync<OperationCanceledException>(async () => await operation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pendingRequestsCanceled, Is.True);
            Assert.That(resourceDisposeCount, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task DisposeAsync_CancelPendingFailureCannotSkipResourceCleanup()
    {
        bool resourcesDisposed = false;
        var lifetime = new AsyncOperationLifetime(
            static () => throw new InvalidOperationException("cancel failed"),
            () =>
            {
                resourcesDisposed = true;
                return ValueTask.CompletedTask;
            });

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await lifetime.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.That(resourcesDisposed, Is.True);
    }

    [Test]
    public async Task DisposeAsync_DeadlineDefersResourcesUntilAStubbornOperationReleasesThem()
    {
        var operationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOperation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int resourceDisposeCount = 0;
        var lifetime = new AsyncOperationLifetime(
            static () => { },
            () =>
            {
                resourceDisposeCount++;
                return ValueTask.CompletedTask;
            },
            TimeSpan.FromMilliseconds(100));
        Task operation = lifetime.RunAsync(async _ =>
        {
            operationStarted.TrySetResult();
            await releaseOperation.Task;
        });
        await operationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var stopwatch = Stopwatch.StartNew();
        await lifetime.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        stopwatch.Stop();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
            Assert.That(resourceDisposeCount, Is.Zero,
                "Resources must remain alive while the admitted operation can still use them.");
        }

        releaseOperation.TrySetResult();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => resourceDisposeCount == 1, TimeSpan.FromSeconds(5));
        Assert.That(resourceDisposeCount, Is.EqualTo(1));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (!condition())
        {
            await Task.Delay(10, cancellation.Token);
        }
    }
}
