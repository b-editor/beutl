using Beutl.Helpers;

namespace Beutl.Models;

internal sealed class BufferedPlayerWaitGate
{
    private readonly Func<bool> _shouldStop;
    private volatile CancellationTokenSource? _token;

    public BufferedPlayerWaitGate(Func<bool> shouldStop)
    {
        _shouldStop = shouldStop;
    }

    public void Cancel()
    {
        _token.CancelIgnoringDisposed();
    }

    public bool Publish(CancellationTokenSource cts)
    {
        _token = cts;

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
        if (ReferenceEquals(_token, cts))
        {
            _token = null;
        }
    }
}
