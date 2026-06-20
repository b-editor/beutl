namespace Beutl.Helpers;

internal static class CancellationTokenSourceHelper
{
    // Tolerates a racing thread disposing the CTS while this one still holds the stale published reference.
    internal static void CancelIgnoringDisposed(this CancellationTokenSource? cts)
    {
        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
