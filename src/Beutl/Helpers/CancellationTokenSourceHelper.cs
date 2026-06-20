namespace Beutl.Helpers;

internal static class CancellationTokenSourceHelper
{
    internal static void CancelIgnoringDisposed(CancellationTokenSource? cts)
    {
        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A waiter (WaitRender/WaitTimer) disposes its own per-wait CTS on return while the producer loop, the
            // isPlaying subscription, or Dispose may still hold the stale published reference and cancel it.
            // Swallowing the race is intentional: the canceller never owns the CTS, so there is nothing to claim via
            // Interlocked, and locking the per-frame cancel against the blocking WaitOne would only trade one rare
            // exception for hot-path contention.
        }
    }
}
