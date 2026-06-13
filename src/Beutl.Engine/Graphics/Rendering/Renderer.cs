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
    private readonly FpsText _fpsText = new();
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
        FrameSize = new PixelSize(width, height);
        RenderScale = renderScale;
        MaxWorkingScale = maxWorkingScale;
        DeviceSize = new PixelSize(
            (int)MathF.Ceiling(width * renderScale),
            (int)MathF.Ceiling(height * renderScale));
        (_immediateCanvas, _surface) = RenderThread.Dispatcher.Invoke(() =>
        {
            RenderTarget surface = RenderTarget.Create(DeviceSize.Width, DeviceSize.Height)
                                   ?? throw new InvalidOperationException(
                                       $"Could not create a canvas of this size. (width: {DeviceSize.Width}, height: {DeviceSize.Height})");

            var canvas = new ImmediateCanvas(surface, renderScale, maxWorkingScale,
                logicalSize: FrameSize.ToSize(1));
            return (canvas, surface);
        });
    }

    ~Renderer()
    {
        if (!IsDisposed)
        {
            OnDispose(false);
            _immediateCanvas.Dispose();
            _surface.Dispose();
            ClearAllCaches();
            DisposeAllEntries();

            IsDisposed = true;
        }
    }

    public bool IsDisposed { get; private set; }

    public bool IsGraphicsRendering { get; private set; }

    public bool DrawFps
    {
        get => _fpsText.DrawFps;
        set => _fpsText.DrawFps = value;
    }

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

    /// <summary>
    /// The output scale <c>s_out</c> this renderer targets (feature 003): device pixels per logical
    /// unit at the root. <c>1.0</c> = logical == device. <see cref="FrameSize"/> stays logical.
    /// </summary>
    public float RenderScale { get; }

    /// <summary>
    /// The working-scale ceiling for this renderer (feature 003, FR-037): preview passes <c>2 × s_out</c>
    /// to bound interactive working scale; export passes a generous-but-finite <c>max(8, 4 × s_out)</c>.
    /// The constructor default <c>+∞</c> (no ceiling) is for non-render-request callers only — production
    /// render requests always seed a finite value. It only caps boundaries that resolve a working scale
    /// above it.
    /// </summary>
    public float MaxWorkingScale { get; }

    /// <summary>The physical backing-surface size, <c>ceil(FrameSize × RenderScale)</c>.</summary>
    public PixelSize DeviceSize { get; }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            OnDispose(true);
            _immediateCanvas.Dispose();
            _surface.Dispose();
            ClearAllCaches();
            DisposeAllEntries();
            GC.SuppressFinalize(this);

            IsDisposed = true;
        }
    }

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

            using (_fpsText.StartRender(_immediateCanvas))
            using (_immediateCanvas.Push())
            {
                _immediateCanvas.Clear();

                // Root output scale (feature 003): the canvas bakes the base CTM CreateScale(RenderScale)
                // at construction, mapping logical content onto the ceil(FrameSize × RenderScale) device
                // surface — vector / text / Skia-filter content re-rasterizes crisply for free. RenderScale
                // == 1 is a true no-op base (byte-identical). The FPS overlay re-enters device space itself
                // (FpsText.FpsDrawer.Dispose -> PushDeviceSpace) so it stays unscaled despite the pinned base.
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
            using var ctx = new GraphicsContext2D(entry.Node, FrameSize.ToSize(1), RenderScale);
            drawable.Render(ctx, resource);
        }

        RevalidateAll(entry.Node);
        var processor = new RenderNodeProcessor(entry.Node, CacheOptions.IsEnabled, RenderScale, MaxWorkingScale);
        var ops = processor.PullToRoot();
        Rect bounds = Rect.Empty;
        foreach (var op in ops)
        {
            op.Render(_immediateCanvas);
            bounds = bounds.Union(op.Bounds);
            op.Dispose();
        }

        entry.Bounds = bounds;
        // Forward this renderer's scale pair so the cache rasterizes at the render density under the
        // FR-037 ceiling (not at the processor defaults density-1 / +∞).
        RenderNodeCacheHelper.MakeCache(entry.Node, CacheOptions, RenderScale, MaxWorkingScale);
        return entry;
    }

    private void AddDetachedHandler(Drawable drawable)
    {
        var weakRef = new WeakReference<Renderer>(this);

        void Handler(object? sender, HierarchyAttachmentEventArgs e)
        {
            if (sender is not Drawable senderDrawable) return;

            if (weakRef.TryGetTarget(out Renderer? renderer)
                && renderer._nodeCache.TryGetValue(senderDrawable, out Entry? entry))
            {
                RenderNodeCacheHelper.ClearCache(entry.Node);
                entry.Dispose();
                renderer._nodeCache.Remove(senderDrawable);
            }

            senderDrawable.DetachedFromHierarchy -= Handler;
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
                using var ctx = new GraphicsContext2D(entry.Node, FrameSize.ToSize(1), RenderScale);
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
            // Pull with the SAME scale pair as the render pass: Process is scale-stateful (Scene3D resizes
            // its Renderer3D, particles re-rasterize on a scale flip), so a default-scale pull here would
            // thrash that state every hit test and escape the FR-037 ceiling. Hit-test results are logical,
            // so they are unchanged by this.
            var processor = new RenderNodeProcessor(entry.Node, CacheOptions.IsEnabled, RenderScale, MaxWorkingScale);
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
            // Same scale pair as the render pass — see HitTest for why a default-scale pull is wrong here.
            var processor = new RenderNodeProcessor(e.Node, CacheOptions.IsEnabled, RenderScale, MaxWorkingScale);
            var ops = processor.PullToRoot();
            Rect bounds = Rect.Empty;
            foreach (var op in ops)
            {
                bounds = bounds.Union(op.Bounds);
                op.Dispose();
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

    public void ClearAllCaches()
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
