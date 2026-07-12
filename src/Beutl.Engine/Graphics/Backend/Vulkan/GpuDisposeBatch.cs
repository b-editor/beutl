using SkiaSharp;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Batches the per-texture GPU drain <see cref="VulkanTexture2D.Dispose"/> performs. Destroying a Vulkan-backed
/// buffer must first complete every recorded-but-unflushed Skia op that still references its image (the SwiftShader
/// use-after-free fix); done per texture, an N-buffer pool eviction issues N queue-submit + fence-wait drains. A
/// batch scope collapses that to one: the first texture destroyed inside the scope drains once — while every image
/// in the batch is still alive — and the rest skip their drain, so the whole batch is torn down behind a single
/// sync. Outside a scope <see cref="VulkanTexture2D.Dispose"/> keeps draining itself (a lone, non-pool disposal).
/// The scope depth is thread-scoped: the pool opens it on the render thread, where the eviction disposals run
/// synchronously, so a cross-thread teardown that dispatches disposals simply falls back to the per-texture drain.
/// </summary>
internal static class GpuDisposeBatch
{
    [ThreadStatic]
    private static int s_depth;

    [ThreadStatic]
    private static bool s_drained;

    /// <summary>Test seam: total drain-flushes issued (one per batch scope, plus one per non-batched dispose).</summary>
    internal static long FlushCount { get; private set; }

    internal static bool IsActive => s_depth > 0;

    /// <summary>
    /// Opens a batch on the current thread: the first <see cref="DrainBeforeDestroy"/> inside drains once and the
    /// rest are suppressed until the returned scope is disposed. Nesting-safe; only the outermost scope drains.
    /// The drain is lazy, so an eviction sweep that removes nothing issues no flush.
    /// </summary>
    internal static Scope Begin()
    {
        if (s_depth++ == 0)
            s_drained = false;

        return default;
    }

    /// <summary>
    /// Completes all pending Skia work before the caller destroys its image handles. In a batch this drains only on
    /// the first call (every batch image is still alive at that point) and is a no-op afterwards; outside a batch it
    /// drains for that single texture, preserving the per-texture behavior.
    /// </summary>
    internal static void DrainBeforeDestroy(GRContext? skiaContext)
    {
        // A destroyed context has nothing to drain, and it must not consume the batch's single drain — the batch's
        // remaining live-context textures would skip theirs, re-opening the teardown UAF the drain exists to prevent.
        if (skiaContext == null)
            return;

        if (s_depth > 0)
        {
            if (s_drained)
                return;

            s_drained = true;
        }

        FlushCount++;
        skiaContext.Flush(submit: true, synchronous: true);
    }

    internal static void ResetFlushCountForTest() => FlushCount = 0;

    internal static bool DrainConsumedForTest => s_drained;

    internal readonly struct Scope : IDisposable
    {
        public void Dispose() => s_depth--;
    }
}
