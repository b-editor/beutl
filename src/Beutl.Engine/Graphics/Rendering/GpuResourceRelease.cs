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

        int started = 0;
        Action? pendingRelease = release;
        EventHandler? shutdownHandler = null;
        void RemoveShutdownHandler()
        {
            EventHandler? handler = Interlocked.Exchange(ref shutdownHandler, null);
            if (handler is not null)
            {
                dispatcher.ShutdownFinished -= handler;
            }
        }

        void Once()
        {
            Volatile.Write(ref started, 1);
            Action? claimedRelease = Interlocked.Exchange(ref pendingRelease, null);
            if (claimedRelease is null)
            {
                return;
            }

            RemoveShutdownHandler();
            claimedRelease();
        }

        void RegisterShutdownFallback()
        {
            EventHandler handler = (_, _) =>
            {
                try
                {
                    Once();
                }
                catch (Exception ex)
                {
                    s_logger.LogWarning(ex, "A GPU resource release failed after dispatcher shutdown");
                }
            };
            dispatcher.ShutdownFinished += handler;
            if (Interlocked.CompareExchange(ref shutdownHandler, handler, null) is not null)
            {
                dispatcher.ShutdownFinished -= handler;
                return;
            }

            if (Volatile.Read(ref pendingRelease) is null)
            {
                RemoveShutdownHandler();
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
            // AggregateException, but callers catch the release's own exception type. GetResult
            // rethrows the original.
            if (((IAsyncResult)queued).AsyncWaitHandle.WaitOne(s_slice))
            {
                queued.GetAwaiter().GetResult();
                return;
            }

            // Already running on the owner thread: the deadline guards work that never starts, not
            // a slow release, and returning would report resources free that are still being torn down.
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

        RegisterShutdownFallback();
        if (dispatcher.HasShutdownFinished)
        {
            Once();
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

    public static void RunRequired(Dispatcher dispatcher, Action operation)
        => RunRequired(dispatcher, () =>
        {
            operation();
            return true;
        });

    public static T RunRequired<T>(Dispatcher dispatcher, Func<T> operation)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(operation);

        if (dispatcher.HasShutdownStarted)
        {
            throw new InvalidOperationException("The render dispatcher is shutting down.");
        }

        if (dispatcher.CheckAccess())
        {
            return operation();
        }

        using var cancellation = new CancellationTokenSource();
        int claim = 0;
        Func<T>? pendingOperation = operation;
        Task<T> queued = dispatcher.InvokeAsync(() =>
        {
            if (Interlocked.CompareExchange(ref claim, 1, 0) != 0)
            {
                return default!;
            }

            Func<T> claimedOperation = Interlocked.Exchange(ref pendingOperation, null)!;
            return claimedOperation();
        }, ct: cancellation.Token);

        while (true)
        {
            if (((IAsyncResult)queued).AsyncWaitHandle.WaitOne(s_slice))
            {
                return queued.GetAwaiter().GetResult();
            }

            if (Volatile.Read(ref claim) == 1)
            {
                return queued.GetAwaiter().GetResult();
            }

            if (dispatcher.HasShutdownStarted)
            {
                if (Interlocked.CompareExchange(ref claim, 2, 0) != 0)
                {
                    return queued.GetAwaiter().GetResult();
                }

                Interlocked.Exchange(ref pendingOperation, null);
                cancellation.Cancel();
                throw new InvalidOperationException("The render dispatcher shut down before the operation started.");
            }
        }
    }

    public static void DispatchFinalizer(Dispatcher? dispatcher, Action release)
    {
        ArgumentNullException.ThrowIfNull(release);

        if (dispatcher is null || dispatcher.CheckAccess() || dispatcher.HasShutdownFinished)
        {
            ReleaseFromFinalizer(release);
            return;
        }

        Action? pendingRelease = release;
        EventHandler? shutdownHandler = null;
        void Once()
        {
            Action? claimedRelease = Interlocked.Exchange(ref pendingRelease, null);
            if (claimedRelease is null)
            {
                return;
            }

            if (shutdownHandler is not null)
            {
                dispatcher.ShutdownFinished -= shutdownHandler;
                shutdownHandler = null;
            }

            ReleaseFromFinalizer(claimedRelease);
        }

        shutdownHandler = (_, _) => Once();
        dispatcher.ShutdownFinished += shutdownHandler;

        if (dispatcher.HasShutdownFinished)
        {
            Once();
            return;
        }

        if (dispatcher.HasShutdownStarted)
        {
            return;
        }

        try
        {
            dispatcher.Dispatch(Once, ct: CancellationToken.None);
        }
        catch (Exception ex)
        {
            s_logger.LogDebug(ex, "Could not dispatch finalizer-driven GPU resource cleanup");
            if (dispatcher.HasShutdownFinished)
            {
                Once();
            }
        }
    }

    private static void ReleaseFromFinalizer(Action release)
    {
        try
        {
            release();
        }
        catch (Exception ex)
        {
            s_logger.LogDebug(ex, "Finalizer-driven GPU resource cleanup failed");
        }
    }
}
