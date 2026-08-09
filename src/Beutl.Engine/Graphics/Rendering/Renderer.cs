using System.Runtime.CompilerServices;
using Beutl.Composition;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Rendering;

public class Renderer : IRenderer
{
    private static readonly ILogger s_logger = Log.CreateLogger<Renderer>();

    private readonly ImmediateCanvas _immediateCanvas;
    private readonly RenderTarget _surface;
    private readonly ConditionalWeakTable<Drawable, Entry> _nodeCache = new();
    private readonly List<Entry> _allCurrentEntries = [];
    private RenderCacheOptions _cacheOptions = RenderCacheOptions.CreateFromGlobalConfiguration();

    private class Entry(DrawableRenderNode node) : IDisposable
    {
        ~Entry()
        {
            Dispose();
        }

        public DrawableRenderNode Node { get; } = node;

        public Rect Bounds { get; set; }

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (!IsDisposed)
            {
                Node.Dispose();
                IsDisposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }

    public Renderer(int width, int height, float renderScale = 1f, float maxWorkingScale = float.PositiveInfinity)
    {
        float outputScale = float.IsFinite(renderScale) && renderScale > 0f ? renderScale : 1f;
        float maxScale = RenderNodeContext.SanitizeMaxWorkingScale(maxWorkingScale);
        FrameSize = new PixelSize(width, height);
        OutputScale = outputScale;
        MaxWorkingScale = maxScale;
        DeviceSize = new PixelSize(
            (int)MathF.Ceiling(width * outputScale),
            (int)MathF.Ceiling(height * outputScale));
        (_immediateCanvas, _surface) = RenderThread.Dispatcher.Invoke(() =>
        {
            RenderTarget surface = RenderTarget.Create(DeviceSize.Width, DeviceSize.Height)
                                   ?? throw new InvalidOperationException(
                                       $"Could not create a canvas of this size. (width: {DeviceSize.Width}, height: {DeviceSize.Height})");

            var canvas = new ImmediateCanvas(surface, outputScale, maxScale,
                logicalSize: FrameSize.ToSize(1));
            return (canvas, surface);
        });
    }

    ~Renderer()
    {
        // A finalizer must never throw. Each step is guarded independently so a failure cannot
        // skip releasing the GPU surface.
        if (IsDisposed)
            return;

        static void SafeStep(string step, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                s_logger.LogDebug(ex, "Renderer finalizer: {Step} threw during last-resort disposal", step);
            }
        }

        void ReleaseGpuResources()
        {
            SafeStep(nameof(_immediateCanvas), () => _immediateCanvas?.Dispose());
            SafeStep(nameof(_surface), () => _surface?.Dispose());
            // Core, not the public wrapper: this already runs on the render thread, or in place
            // because it is gone — either way the wrapper's bounded wait must not re-enter here.
            SafeStep(nameof(ClearAllCachesCore), ClearAllCachesCore);
            SafeStep(nameof(DisposeAllEntries), DisposeAllEntries);
        }

        _isDisposed = true;
        SafeStep(nameof(OnDispose), () => OnDispose(false));

        // The finalizer thread does not own these GPU resources and must not block waiting for the
        // thread that does, so hand the release over unless that thread is already gone. Finished,
        // not Started: the latter is set before the operation already running has returned.
        if (RenderThread.Dispatcher.HasShutdownFinished)
        {
            ReleaseGpuResources();
        }
        else
        {
            SafeStep(nameof(ReleaseGpuResources), () => RenderThread.Dispatcher.Dispatch(ReleaseGpuResources));
        }
    }

    private volatile bool _isDisposed;

    public bool IsDisposed => _isDisposed;

    public bool IsGraphicsRendering { get; private set; }

    public RenderCacheOptions CacheOptions
    {
        get => _cacheOptions;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            ClearAllCaches();
            _cacheOptions = value;
        }
    }

    public TimeSpan Time { get; internal set; }

    public PixelSize FrameSize { get; }

    /// <summary>Output scale <c>s_out</c> (device px per logical unit). <see cref="FrameSize"/> stays logical.</summary>
    public float OutputScale { get; }

    /// <summary>Working-scale ceiling. Preview: <c>2 * s_out</c>; export: <c>+Inf</c>.</summary>
    public float MaxWorkingScale { get; }

    /// <summary>
    /// The physical backing-surface size, <c>ceil(FrameSize × OutputScale)</c>.
    /// Ceiling preserves fractional edge pixels; only place OutputScale sizes a surface.
    /// </summary>
    public PixelSize DeviceSize { get; }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            _isDisposed = true;
            OnDispose(true);
            // The canvas, the surface and every cached node hold GPU resources owned by the render
            // thread, so tear them down there.
            GpuResourceRelease.Run(RenderThread.Dispatcher, () =>
            {
                _immediateCanvas.Dispose();
                _surface.Dispose();
                ClearAllCachesCore();
                DisposeAllEntries();
            });
            GC.SuppressFinalize(this);
        }
    }

    /// <remarks><see cref="IsDisposed"/> is already <c>true</c> when this method is called.</remarks>
    protected virtual void OnDispose(bool disposing)
    {
    }

    public void Render(CompositionFrame frame)
    {
        RenderThread.Dispatcher.VerifyAccess();
        if (IsGraphicsRendering)
            return;

        try
        {
            IsGraphicsRendering = true;
            Time = frame.Time.Start;
            ClearFrame();

            using (_immediateCanvas.Push())
            {
                _immediateCanvas.Clear();

                RenderObjects(frame);
            }
        }
        finally
        {
            IsGraphicsRendering = false;
        }
    }

    private void RenderObjects(CompositionFrame frame)
    {
        foreach (var obj in frame.Objects)
        {
            if (obj is not Drawable.Resource drawableResource)
                continue;
            var entry = RenderDrawable(drawableResource);
            _allCurrentEntries.Add(entry);
        }
    }

    private Entry RenderDrawable(Drawable.Resource resource)
    {
        var drawable = resource.GetOriginal();
        Entry entry;
        bool shouldRender;

        if (!_nodeCache.TryGetValue(drawable, out entry!))
        {
            AddDetachedHandler(drawable);
            entry = new Entry(new DrawableRenderNode(resource));
            _nodeCache.Add(drawable, entry);
            shouldRender = true;
        }
        else
        {
            shouldRender = entry.Node.Update(resource);
        }

        if (shouldRender)
        {
            using var ctx = new GraphicsContext2D(entry.Node, FrameSize.ToSize(1), OutputScale);
            drawable.Render(ctx, resource);
        }

        RevalidateAll(entry.Node);
        entry.Node.PrepareForProcess(_immediateCanvas);
        var processor = new RenderNodeProcessor(entry.Node, CacheOptions.IsEnabled, OutputScale, MaxWorkingScale);
        var ops = processor.PullToRoot();
        Rect bounds = Rect.Empty;
        int consumed = 0;
        try
        {
            foreach (var op in ops)
            {
                op.Render(_immediateCanvas);
                bounds = bounds.Union(op.Bounds);
                // consumed++ trails op.Bounds (a throw site) so a throw before op.Dispose leaves
                // this op in the cleanup sweep below.
                consumed++;
                op.Dispose();
            }
        }
        catch
        {
            RenderNodeOperation.DisposeAll(ops.AsSpan(consumed));
            throw;
        }

        entry.Bounds = bounds;
        RenderNodeCacheHelper.MakeCache(entry.Node, CacheOptions, OutputScale, MaxWorkingScale);
        return entry;
    }

    private void AddDetachedHandler(Drawable drawable)
    {
        var weakRef = new WeakReference<Renderer>(this);

        void Handler(object? sender, HierarchyAttachmentEventArgs e)
        {
            if (sender is not Drawable senderDrawable) return;

            senderDrawable.DetachedFromHierarchy -= Handler;

            // Detaching happens on the edit thread, but the entry's cache is GPU state owned by the
            // render thread. Queued rather than awaited so an edit never blocks behind a frame.
            RenderThread.Dispatcher.Dispatch(() =>
            {
                // Nothing awaits this and the dispatcher has no UnhandledException handler, so an
                // escaping exception would rethrow out of its loop and kill the render thread.
                if (!weakRef.TryGetTarget(out Renderer? renderer)
                    || !renderer._nodeCache.TryGetValue(senderDrawable, out Entry? entry))
                {
                    return;
                }

                // Independently guarded: a failed cache clear must not skip the entry's own
                // disposal, which is the only thing that releases its node.
                try
                {
                    RenderNodeCacheHelper.ClearCache(entry.Node);
                }
                catch (Exception ex)
                {
                    s_logger.LogWarning(ex, "Failed to clear the render cache of a detached drawable");
                }

                try
                {
                    entry.Dispose();
                }
                catch (Exception ex)
                {
                    s_logger.LogWarning(ex, "Failed to dispose the render entry of a detached drawable");
                }

                // The handler is already unsubscribed, so an entry left behind would be reused by a
                // reattached drawable and never retried.
                renderer._nodeCache.Remove(senderDrawable);
            });
        }

        drawable.DetachedFromHierarchy += Handler;
    }

    private static void RevalidateAll(RenderNode current)
    {
        RenderNodeCache cache = current.Cache;

        if (current is ContainerRenderNode c)
        {
            foreach (RenderNode item in c.Children)
            {
                RevalidateAll(item);
            }
        }

        cache.IncrementRenderCount();
        current.HasChanges = false;
        if (cache.IsCached && !RenderNodeCacheHelper.CanCacheRecursive(current))
        {
            cache.Invalidate();
        }
    }

    private void ClearFrame()
    {
        _allCurrentEntries.Clear();
    }

    public void UpdateFrame(CompositionFrame frame)
    {
        RenderThread.Dispatcher.VerifyAccess();
        Time = frame.Time.Start;
        ClearFrame();

        foreach (var obj in frame.Objects)
        {
            if (obj is not Drawable.Resource drawableResource)
                continue;

            var drawable = drawableResource.GetOriginal();
            Entry entry;
            bool shouldRender;

            if (!_nodeCache.TryGetValue(drawable, out entry!))
            {
                AddDetachedHandler(drawable);
                entry = new Entry(new DrawableRenderNode(drawableResource));
                _nodeCache.Add(drawable, entry);
                shouldRender = true;
            }
            else
            {
                shouldRender = entry.Node.Update(drawableResource);
            }

            if (shouldRender)
            {
                using var ctx = new GraphicsContext2D(entry.Node, FrameSize.ToSize(1), OutputScale);
                drawable.Render(ctx, drawableResource);
            }

            RevalidateAll(entry.Node);
            _allCurrentEntries.Add(entry);
        }
    }

    public Drawable? HitTest(CompositionFrame frame, Point point)
    {
        RenderThread.Dispatcher.VerifyAccess();
        UpdateFrame(frame);

        for (int i = _allCurrentEntries.Count - 1; i >= 0; i--)
        {
            Entry entry = _allCurrentEntries[i];
            // Same scale pair as the render pass to avoid thrashing scale-stateful nodes.
            var processor = new RenderNodeProcessor(entry.Node, CacheOptions.IsEnabled, OutputScale, MaxWorkingScale);
            var arr = processor.PullToRoot();
            try
            {
                if (arr.Any(op => op.HitTest(point)))
                {
                    return entry.Node.Drawable?.Resource.GetOriginal();
                }
            }
            finally
            {
                foreach (var op in arr)
                {
                    op.Dispose();
                }
            }
        }

        return null;
    }

    public Rect[] GetBoundaries(int zIndex)
    {
        return [.. _allCurrentEntries.Where(e => e.Node.Drawable?.Resource.GetOriginal().ZIndex == zIndex).Select(e => e.Bounds)];
    }

    public Rect? GetBoundary(Drawable drawable)
    {
        RenderThread.Dispatcher.VerifyAccess();
        if (_nodeCache.TryGetValue(drawable, out Entry? entry))
        {
            if (_allCurrentEntries.Contains(entry))
            {
                return entry.Bounds;
            }
            // An entry exists but is not included in the current frame (stale). Suggests a draw-lifecycle mismatch.
            if (s_logger.IsEnabled(LogLevel.Debug))
            {
                s_logger.LogDebug(
                    "GetBoundary: stale entry for {DrawableType}#{DrawableHash:X} (cached but not in current frame).",
                    drawable.GetType().Name, RuntimeHelpers.GetHashCode(drawable));
            }
            return null;
        }

        // Cache miss that also occurs in normal operation (not yet drawn or already evicted).
        if (s_logger.IsEnabled(LogLevel.Trace))
        {
            s_logger.LogTrace(
                "GetBoundary: drawable {DrawableType}#{DrawableHash:X} not in render-node cache.",
                drawable.GetType().Name, RuntimeHelpers.GetHashCode(drawable));
        }
        return null;
    }

    public Rect[] RecalculateBoundaries(int zIndex)
    {
        return [.. _allCurrentEntries.Where(e => e.Node.Drawable?.Resource.GetOriginal().ZIndex == zIndex).Select(e =>
        {
            var processor = new RenderNodeProcessor(e.Node, CacheOptions.IsEnabled, OutputScale, MaxWorkingScale);
            var ops = processor.PullToRoot();
            Rect bounds = Rect.Empty;
            int consumed = 0;
            try
            {
                foreach (var op in ops)
                {
                    bounds = bounds.Union(op.Bounds);
                    consumed++;
                    op.Dispose();
                }
            }
            catch
            {
                RenderNodeOperation.DisposeAll(ops.AsSpan(consumed));
                throw;
            }
            e.Bounds = bounds;
            return bounds;
        })];
    }

    public DrawableRenderNode? FindRenderNode(Drawable drawable)
    {
        if (_nodeCache.TryGetValue(drawable, out Entry? entry))
        {
            return entry.Node;
        }

        // Recursive search
        foreach (var item in _nodeCache)
        {
            if (item.Value.Node is not ContainerRenderNode container) continue;

            var result = FindChildRenderNode(container, drawable);
            if (result != null)
                return result;
        }

        return null;
    }

    private static DrawableRenderNode? FindChildRenderNode(ContainerRenderNode container, Drawable drawable)
    {
        foreach (var child in container.Children)
        {
            if (child is ContainerRenderNode childContainer)
            {
                var result = FindChildRenderNode(childContainer, drawable);
                if (result != null)
                    return result;
            }
            else if (child is DrawableRenderNode childDrawable &&
                     childDrawable.Drawable?.Resource.GetOriginal() == drawable)
            {
                return childDrawable;
            }
        }

        return null;
    }

    public Bitmap Snapshot()
    {
        RenderThread.Dispatcher.VerifyAccess();
        return _surface.Snapshot();
    }

    /// <summary>
    /// Reads the current surface into an existing <paramref name="destination"/> bitmap, reusing it
    /// instead of allocating a fresh snapshot. See <see cref="RenderTarget.SnapshotInto(Bitmap)"/>.
    /// </summary>
    public void SnapshotInto(Bitmap destination)
    {
        RenderThread.Dispatcher.VerifyAccess();
        _surface.SnapshotInto(destination);
    }

    /// <summary>
    /// Allocates a bitmap in the format <see cref="Snapshot()"/> produces, suitable as a reusable
    /// destination for <see cref="SnapshotInto(Bitmap)"/>. See <see cref="RenderTarget.CreateSnapshotBitmap()"/>.
    /// </summary>
    public Bitmap CreateSnapshotBitmap() => _surface.CreateSnapshotBitmap();

    // Callers reach this from the UI thread (a cache-option change, an export teardown), but the
    // cached nodes hold GPU resources the render thread owns.
    public void ClearAllCaches()
    {
        GpuResourceRelease.Run(RenderThread.Dispatcher, ClearAllCachesCore);
    }

    private void ClearAllCachesCore()
    {
        var entries = _nodeCache.ToArray();
        _nodeCache.Clear();
        foreach (var item in entries)
        {
            RenderNodeCacheHelper.ClearCache(item.Value.Node);
            item.Value.Dispose();
        }
    }

    private void DisposeAllEntries()
    {
        foreach (var item in _nodeCache)
        {
            // Compositor側でDisposeされるのでResourceはDisposeせず、NodeだけがDisposeされるようにする
            item.Value.Dispose();
        }
        _nodeCache.Clear();
    }

    public static ImmediateCanvas GetInternalCanvas(Renderer renderer)
    {
        return renderer._immediateCanvas;
    }

    public static RenderTarget GetInternalRenderTarget(Renderer renderer)
    {
        return renderer._surface;
    }
}
