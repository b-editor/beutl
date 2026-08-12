using Beutl.Graphics.Backend;

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
    /// <returns><see langword="true"/> when a context-wide flush was performed.</returns>
    public static bool FlushAndDrain()
    {
        if (s_pending.Count == 0 || s_draining || !RenderThread.Dispatcher.CheckAccess())
        {
            return false;
        }

        bool flushed = false;
        if (GraphicsContextFactory.SharedContext is { } context)
        {
            context.SkiaContext.Flush(true, true);
            flushed = true;
        }

        Drain();
        return flushed;
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
