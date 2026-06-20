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
            // Wait methods can dispose a published CTS while a racing wakeup still holds the stale reference.
        }
    }
}
