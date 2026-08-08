using Beutl.Threading;

namespace Beutl.Graphics.Rendering;

internal static class GpuResourceRelease
{
    // A caller that waits forever is worse than a GPU resource released on the wrong thread: the
    // dispatcher can stop between the shutdown check and the enqueue, and a wedged render thread
    // would otherwise hang Dispose (or the whole finalizer queue). Bounded, then release in place.
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Runs <paramref name="release"/> on <paramref name="dispatcher"/>, falling back to the calling
    /// thread if that dispatcher is gone or does not pick the work up in time. Runs exactly once even
    /// when a timed-out operation is later drained by the dispatcher after all.
    /// </summary>
    public static void Run(Dispatcher? dispatcher, Action release)
    {
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            release();
            return;
        }

        int claimed = 0;
        void Once()
        {
            if (Interlocked.Exchange(ref claimed, 1) == 0)
            {
                release();
            }
        }

        if (dispatcher.HasShutdownStarted)
        {
            Once();
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(s_timeout);
            dispatcher.Invoke(Once, ct: cts.Token);
        }
        catch (OperationCanceledException)
        {
            Once();
        }
    }
}
