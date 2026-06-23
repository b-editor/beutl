using Beutl.Helpers;

namespace Beutl.Models;

internal sealed class BufferedPlayerWaitGate
{
    private readonly Func<bool> _shouldStop;
    private CancellationTokenSource? _token;

    public BufferedPlayerWaitGate(Func<bool> shouldStop)
    {
        _shouldStop = shouldStop;
    }

    public void Cancel()
    {
        Volatile.Read(ref _token).CancelIgnoringDisposed();
    }

    public bool Publish(CancellationTokenSource cts)
    {
        Volatile.Write(ref _token, cts);

        // Re-check stop state after publishing the token. A stopping thread may have observed the old token slot
        // before this publish; the fence ensures this waiter either sees the stop flag or the stopper sees this token.
        Interlocked.MemoryBarrier();
        if (!_shouldStop())
        {
            return true;
        }

        Clear(cts);
        return false;
    }

    public void Clear(CancellationTokenSource cts)
    {
        // A plain check-then-clear could wipe a concurrently-published newer token and lose its wakeup.
        Interlocked.CompareExchange(ref _token, null, cts);
    }
}
