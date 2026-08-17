using Beutl.Api;

namespace Beutl.UnitTests.Api;

[TestFixture]
[NonParallelizable]
public sealed class MyAsyncLockTests
{
    [Test]
    public void LockAsync_PreCanceledToken_ThrowsWithoutAcquiringTheLock()
    {
        var asyncLock = new MyAsyncLock();
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await asyncLock.LockAsync(cancellationTokenSource.Token));

        // The lock must still be acquirable after the canceled attempt.
        using IDisposable releaser = asyncLock.LockAsync().GetAwaiter().GetResult();
    }

    [Test]
    public async Task LockAsync_ContendedCanceledAcquisition_DoesNotLeakTheLock()
    {
        var asyncLock = new MyAsyncLock();
        IDisposable first = asyncLock.LockAsync().GetAwaiter().GetResult();
        using var cancellationTokenSource = new CancellationTokenSource();

        // A contended acquisition starts waiting on the semaphore.
        Task<IDisposable> second = asyncLock.LockAsync(cancellationTokenSource.Token);

        // Canceling while the acquisition waits must propagate instead of returning a
        // releaser, and must not leave the lock permanently held.
        cancellationTokenSource.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await second.WaitAsync(TimeSpan.FromSeconds(5)));

        // The lock must still be acquirable after the canceled contended attempt.
        first.Dispose();
        using IDisposable third = asyncLock.LockAsync().GetAwaiter().GetResult();
    }
}
