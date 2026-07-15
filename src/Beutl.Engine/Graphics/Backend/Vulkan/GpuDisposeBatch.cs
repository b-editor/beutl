using System.Threading;
using SkiaSharp;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Batches the per-texture GPU drain <see cref="VulkanTexture2D.Dispose"/> performs. Destroying a Vulkan-backed
/// buffer must first complete every recorded-but-unflushed Skia op that still references its image (the SwiftShader
/// use-after-free fix); done per texture, an N-buffer pool eviction issues N queue-submit + fence-wait drains. A
/// batch scope collapses that to one per live GRContext: the first texture for each context drains while every image
/// in that context's batch is still alive, and later textures for the same context skip their drain. Outside a scope
/// <see cref="VulkanTexture2D.Dispose"/> keeps draining itself (a lone, non-pool disposal).
/// The scope depth is thread-scoped: the pool opens it on the render thread, where the eviction disposals run
/// synchronously, so a cross-thread teardown that dispatches disposals simply falls back to the per-texture drain.
/// </summary>
internal static class GpuDisposeBatch
{
    [ThreadStatic]
    private static int s_depth;

    [ThreadStatic]
    private static List<object>? s_drainedContexts;

    [ThreadStatic]
    private static Action? s_drainFailureForTest;

    /// <summary>Test seam: total drain-flushes issued (one per context per batch, plus one per non-batched dispose).</summary>
    private static long s_flushCount;

    internal static long FlushCount => Interlocked.Read(ref s_flushCount);

    internal static bool IsActive => s_depth > 0;

    /// <summary>
    /// Opens a batch on the current thread: the first <see cref="DrainBeforeDestroy"/> for each context drains once
    /// and later calls for that context are suppressed until the returned scope is disposed. Nesting-safe.
    /// The drain is lazy, so an eviction sweep that removes nothing issues no flush.
    /// </summary>
    internal static Scope Begin()
    {
        if (s_depth++ == 0)
            (s_drainedContexts ??= []).Clear();

        return default;
    }

    /// <summary>
    /// Completes all pending Skia work before the caller destroys its image handles. In a batch this drains only on
    /// the first call for each distinct context and is a no-op for later calls to that context; outside a batch it
    /// drains for that single texture, preserving the per-texture behavior.
    /// </summary>
    internal static void DrainBeforeDestroy(GRContext? skiaContext)
    {
        // A destroyed context has nothing to drain, and it must not consume the batch's single drain — the batch's
        // remaining live-context textures would skip theirs, re-opening the teardown UAF the drain exists to prevent.
        if (skiaContext == null)
            return;

        DrainBeforeDestroyCore(
            skiaContext,
            s_drainFailureForTest ?? (() => skiaContext.Flush(submit: true, synchronous: true)));
    }

    internal static void DrainBeforeDestroyForTest(object contextIdentity, Action drain)
        => DrainBeforeDestroyCore(contextIdentity, drain);

    private static void DrainBeforeDestroyCore(object contextIdentity, Action drain)
    {
        bool isBatched = s_depth > 0;
        if (isBatched && s_drainedContexts!.Exists(item => ReferenceEquals(item, contextIdentity)))
            return;

        Interlocked.Increment(ref s_flushCount);
        drain();

        if (isBatched)
            s_drainedContexts!.Add(contextIdentity);
    }

    internal static void ResetFlushCountForTest() => Interlocked.Exchange(ref s_flushCount, 0);

    internal static void SetDrainFailureForTest(Action? failure) => s_drainFailureForTest = failure;

    internal static bool DrainConsumedForTest => s_drainedContexts is { Count: > 0 };

    internal readonly struct Scope : IDisposable
    {
        public void Dispose()
        {
            if (--s_depth == 0)
                s_drainedContexts?.Clear();
        }
    }
}
