using Beutl.Logging;
using Beutl.Threading;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Rendering;

internal static class GpuResourceRelease
{
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(GpuResourceRelease));
    private static readonly TimeSpan s_slice = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan s_deadline = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Runs <paramref name="release"/> on <paramref name="dispatcher"/>, the thread that owns the
    /// GPU resources it frees.
    /// </summary>
    /// <remarks>
    /// Slow and stopped are not the same thing. A live dispatcher that has not got to the operation
    /// yet may be mid-frame and still using what <paramref name="release"/> would tear down, so the
    /// caller stops waiting rather than releasing off-thread — the queued operation still runs when
    /// the dispatcher drains it. Only <see cref="Dispatcher.HasShutdownFinished"/> licenses releasing
    /// here: <c>HasShutdownStarted</c> is set the moment <c>Shutdown()</c> is called and does not wait
    /// for the operation already running, so it would still overlap a live frame. Runs exactly once
    /// across both paths.
    /// </remarks>
    public static void Run(Dispatcher? dispatcher, Action release)
    {
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            release();
            return;
        }

        int claimed = 0;
        int started = 0;
        void Once()
        {
            Volatile.Write(ref started, 1);
            if (Interlocked.Exchange(ref claimed, 1) == 0)
            {
                release();
            }
        }

        if (dispatcher.HasShutdownFinished)
        {
            Once();
            return;
        }

        Task queued = dispatcher.InvokeAsync(Once);
        for (TimeSpan waited = TimeSpan.Zero; waited < s_deadline; waited += s_slice)
        {
            // Waited through the handle, not Task.Wait: that wraps a failed release in an
            // AggregateException, and callers of the formerly inline API catch the release's own
            // exception type. GetResult rethrows the original.
            if (((IAsyncResult)queued).AsyncWaitHandle.WaitOne(s_slice))
            {
                queued.GetAwaiter().GetResult();
                return;
            }

            // Already running on the owner thread: the deadline guards against work that never
            // starts, not against a release that is simply slow, and returning here would tell the
            // caller the resources are free while they are still being torn down.
            if (Volatile.Read(ref started) == 1)
            {
                queued.GetAwaiter().GetResult();
                return;
            }

            // Re-checked every slice, so a shutdown finishing after the check above is still caught.
            if (dispatcher.HasShutdownFinished)
            {
                Once();
                return;
            }
        }

        // The dispatcher may have picked the operation up during the last slice, after the check
        // inside the loop; returning here would abandon a release that is already running.
        if (Volatile.Read(ref started) == 1)
        {
            queued.GetAwaiter().GetResult();
            return;
        }

        s_logger.LogDebug(
            "GPU resource release is still queued after {Deadline}; leaving it to the render thread.",
            s_deadline);

        // InvokeAsync runs the action inside a Task, so a failure lands there rather than in the
        // dispatcher's exception handler. With the caller gone nothing would observe it, and a
        // half-finished cleanup would look like a success.
        _ = queued.ContinueWith(
            static t => s_logger.LogWarning(t.Exception, "A GPU resource release failed after its caller stopped waiting"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
