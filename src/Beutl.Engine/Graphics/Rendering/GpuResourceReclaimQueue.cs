using Beutl.Graphics.Backend;

using SkiaSharp;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Holds GPU-backed render-target resources between their last managed reference going away and the
/// backend finishing the commands that still read them.
/// </summary>
/// <remarks>
/// A GPU <see cref="RenderTarget"/> wraps an <see cref="ITexture2D"/> that Beutl owns and Skia only
/// borrows, so recording a draw from one target into another leaves the source image referenced by
/// work Skia has not submitted yet. Destroying the source in that window hands the driver a freed
/// image. Deferring the destruction until a context-wide flush has submitted and synchronized closes
/// the window without paying for a flush after every draw.
/// </remarks>
internal static class GpuResourceReclaimQueue
{
    /// <summary>Drains early once deferred resources outgrow this, so a render that never reads back stays bounded.</summary>
    private const long PendingByteBudget = 256L * 1024 * 1024;

    private static readonly List<IDisposable> s_pending = [];
    private static long s_pendingBytes;
    private static bool s_draining;

    /// <summary>The number of resources waiting for the backend, for diagnostics and tests.</summary>
    internal static int PendingCount => s_pending.Count;

    /// <summary>
    /// Takes ownership of <paramref name="resource"/> until the next drain.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the queue cannot take it — the caller destroys it itself.
    /// </returns>
    public static bool TryDefer(IDisposable resource, long approximateBytes)
    {
        if (s_draining
            || !RenderThread.Dispatcher.CheckAccess()
            || GraphicsContextFactory.SharedContext is null)
        {
            return false;
        }

        s_pending.Add(resource);
        s_pendingBytes += Math.Max(0, approximateBytes);

        if (s_pendingBytes > PendingByteBudget)
        {
            FlushAndDrain();
        }

        return true;
    }

    /// <summary>
    /// Destroys everything queued so far. The caller guarantees a context-wide flush that submitted
    /// and CPU-synchronized every recorded command has completed.
    /// </summary>
    /// <remarks>
    /// Graphics teardown is the one caller that cannot make that guarantee, and it does not need to: a
    /// queued resource destroys itself through the command pool of the context that still owns it, and a
    /// live pool retires the destroy behind the submission that reads it. What that pool cannot survive is
    /// being asked after its context is gone, when it runs every destroy immediately against a device that
    /// no longer exists - so an unflushed discharge before the release beats a flushed one after it.
    /// </remarks>
    public static void DrainAfterContextSync()
    {
        if (RenderThread.Dispatcher.CheckAccess())
        {
            Drain();
        }
    }

    /// <summary>
    /// Submits and synchronizes the shared context, then destroys everything queued so far.
    /// </summary>
    /// <param name="samplingContext">
    /// The context whose own flush the caller wants to skip, or <see langword="null"/> when the caller only
    /// wants the queue drained and is not about to sample anything.
    /// </param>
    /// <returns>
    /// <see langword="true"/> only when the context-wide flush covered <paramref name="samplingContext"/>
    /// itself. The queue is drained either way; a caller told <see langword="false"/> still has to submit
    /// its own surface, because only the shared context is flushed here and a target from a caller-supplied
    /// factory can live on another one — skipping its flush would let a snapshot read work never submitted.
    /// </returns>
    public static bool FlushAndDrain(GRRecordingContext? samplingContext = null)
    {
        if (s_pending.Count == 0 || s_draining || !RenderThread.Dispatcher.CheckAccess())
        {
            return false;
        }

        bool flushedSamplingContext = false;
        if (GraphicsContextFactory.SharedContext is { } context)
        {
            GRContext shared = context.SkiaContext;
            shared.Flush(true, true);
            flushedSamplingContext = samplingContext is null || ReferenceEquals(shared, samplingContext);
        }

        Drain();
        return flushedSamplingContext;
    }

    private static void Drain()
    {
        if (s_pending.Count == 0 || s_draining) return;

        s_draining = true;
        try
        {
            // Destroy in queue order so a surface is released before the texture it wraps.
            for (int i = 0; i < s_pending.Count; i++)
            {
                try
                {
                    s_pending[i].Dispose();
                }
                catch
                {
                    // A backend teardown failure must not strand the remaining resources.
                }
            }
        }
        finally
        {
            s_pending.Clear();
            s_pendingBytes = 0;
            s_draining = false;
        }
    }
}
