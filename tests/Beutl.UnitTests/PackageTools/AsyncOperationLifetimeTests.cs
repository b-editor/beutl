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
        bool resourcesDisposed = false;
        var lifetime = new AsyncOperationLifetime(
            () => requestsCanceled = true,
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
            Assert.That(requestsCanceled, Is.True);
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
}
