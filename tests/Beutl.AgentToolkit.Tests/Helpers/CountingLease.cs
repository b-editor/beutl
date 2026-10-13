namespace Beutl.AgentToolkit.Tests.Helpers;

// An output operation lease that counts its releases; the callback and Released run on the first one.
internal sealed class CountingLease(Action? onDispose = null) : IDisposable
{
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposeCount;

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    public Task Released => _released.Task;

    public void Dispose()
    {
        if (Interlocked.Increment(ref _disposeCount) == 1)
        {
            onDispose?.Invoke();
            _released.TrySetResult();
        }
    }
}
