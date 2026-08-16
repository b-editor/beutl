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
}
