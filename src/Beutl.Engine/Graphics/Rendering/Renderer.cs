using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
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
    private readonly ConditionalWeakTable<Drawable, Entry> _frameNodeCache = new();
    private readonly ConditionalWeakTable<Drawable, Entry> _auxiliaryNodeCache = new();
    private readonly ConditionalWeakTable<Drawable, DetachedSubscription> _detachedSubscriptions = new();
    private readonly List<Entry> _allCurrentEntries = [];
    private RenderCacheOptions _cacheOptions = RenderCacheOptions.CreateFromGlobalConfiguration();

    /// <summary>Effect-pipeline counters shared by every processor this renderer creates.</summary>
    public PipelineDiagnostics Diagnostics { get; } = new();

    /// <summary>
    /// Render-target pool shared by the render-path processors this renderer creates. Per-renderer and
    /// render-thread-affine (research D4 deviation documented on <see cref="RenderTargetPool"/>); trimmed at
    /// each frame boundary and disposed with the renderer.
    /// </summary>
    private readonly RenderTargetPool _pool = RenderThread.Dispatcher.Invoke(static () => new RenderTargetPool());

    private long _frameIndex;
    private bool _hasCurrentFrame;
    private RenderPullPurpose _currentFramePullPurpose = RenderPullPurpose.Frame;

    internal RenderPullPurpose? CurrentFramePullPurpose
        => _hasCurrentFrame ? _currentFramePullPurpose : null;

    private class Entry(DrawableRenderNode node) : IDisposable
    {
        ~Entry()
        {
            try
            {
                Dispose();
            }
            catch
            {
                // Finalizers must never allow cleanup failures to escape onto the finalizer thread.
            }
        }

        public DrawableRenderNode Node { get; } = node;

        public Rect Bounds { get; set; }

        public bool IsBoundsDirty { get; set; } = true;

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed)
                return;

            IsDisposed = true;
            try
            {
                Node.Dispose();
            }
            finally
            {
                GC.SuppressFinalize(this);
            }
        }
    }

    private sealed class DetachedSubscription(EventHandler<HierarchyAttachmentEventArgs> handler)
    {
        public EventHandler<HierarchyAttachmentEventArgs> Handler { get; } = handler;
    }

    public Renderer(
        int width, int height, RenderIntent renderIntent, float renderScale = 1f,
        float maxWorkingScale = float.PositiveInfinity)
        : this(
            width, height, renderIntent, renderScale, maxWorkingScale,
            RenderPullPurpose.Frame)
    {
    }

    internal Renderer(
        int width,
        int height,
        RenderIntent renderIntent,
        float renderScale,
        float maxWorkingScale,
        RenderPullPurpose pullPurpose)
    {
        float outputScale = float.IsFinite(renderScale) && renderScale > 0f ? renderScale : 1f;
        float maxScale = RenderNodeContext.SanitizeMaxWorkingScale(maxWorkingScale);
        FrameSize = new PixelSize(width, height);
        OutputScale = outputScale;
        MaxWorkingScale = maxScale;
        RenderIntent = RenderPolicyValidation.Validate(renderIntent, nameof(renderIntent));
        PullPurpose = RenderPolicyValidation.Validate(pullPurpose, nameof(pullPurpose));
        DeviceSize = new PixelSize(
            (int)MathF.Ceiling(width * outputScale),
            (int)MathF.Ceiling(height * outputScale));
        (_immediateCanvas, _surface) = RenderThread.Dispatcher.Invoke(() =>
        {
            RenderTarget surface = RenderTarget.Create(DeviceSize.Width, DeviceSize.Height)
                                   ?? throw new InvalidOperationException(
                                       $"Could not create a canvas of this size. (width: {DeviceSize.Width}, height: {DeviceSize.Height})");

            var canvas = new ImmediateCanvas(
                surface, RenderIntent, outputScale, maxScale, logicalSize: FrameSize.ToSize(1),
                pullPurpose: PullPurpose);
            return (canvas, surface);
        });
    }

    private volatile bool _isDisposed;

    public bool IsDisposed => _isDisposed;

    public bool IsGraphicsRendering { get; private set; }

    public RenderCacheOptions CacheOptions
    {
        get => _cacheOptions;
        set
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(value);
            ClearAllCaches();
            _cacheOptions = value;
        }
    }

    public TimeSpan Time { get; internal set; }

    public PixelSize FrameSize { get; }

    /// <summary>Output scale <c>s_out</c> (device px per logical unit). <see cref="FrameSize"/> stays logical.</summary>
    public float OutputScale { get; }

    /// <summary>Working-scale ceiling, independent of <see cref="RenderIntent"/>.</summary>
    public float MaxWorkingScale { get; }

    /// <summary>Explicit preview/delivery failure policy for this renderer.</summary>
    public RenderIntent RenderIntent { get; }

    /// <summary>The purpose forwarded through every render-tree pull owned by this renderer.</summary>
    internal RenderPullPurpose PullPurpose { get; }

    /// <summary>
    /// The physical backing-surface size, <c>ceil(FrameSize × OutputScale)</c>.
    /// Ceiling preserves fractional edge pixels; only place OutputScale sizes a surface.
    /// </summary>
    public PixelSize DeviceSize { get; }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        _isDisposed = true;
        Exception? failure = null;

        void SafeStep(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }

        SafeStep(() => OnDispose(true));
        SafeStep(DetachAllHandlers);
        SafeStep(_pool.Dispose);
        SafeStep(_immediateCanvas.Dispose);
        SafeStep(_surface.Dispose);
        SafeStep(ClearAllCaches);
        SafeStep(DisposeAllEntries);
        GC.SuppressFinalize(this);

        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    /// <remarks><see cref="IsDisposed"/> is already <c>true</c> when this method is called.</remarks>
    protected virtual void OnDispose(bool disposing)
    {
    }

    public void Render(CompositionFrame frame)
    {
        ThrowIfDisposed();
        RenderThread.Dispatcher.VerifyAccess();
        RequireFramePolicy(frame, PullPurpose, "render");
        if (IsGraphicsRendering)
            return;

        try
        {
            IsGraphicsRendering = true;
            Time = frame.Time.Start;
            _pool.Trim(++_frameIndex);
            ClearFrame();

            using (_immediateCanvas.Push())
            {
                _immediateCanvas.Clear();

                RenderObjects(frame);
            }

            _currentFramePullPurpose = frame.PullPurpose;
            _hasCurrentFrame = true;
        }
        catch
        {
            _hasCurrentFrame = false;
            ClearFrame();
            throw;
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
        ConditionalWeakTable<Drawable, Entry> nodeCache = GetNodeCache(PullPurpose);
        Entry? entry = null;
        try
        {
            bool shouldRender;
            if (!nodeCache.TryGetValue(drawable, out entry))
            {
                entry = new Entry(new DrawableRenderNode(resource));
                nodeCache.Add(drawable, entry);
                AddDetachedHandler(drawable);
                shouldRender = true;
            }
            else
            {
                shouldRender = entry.Node.Update(resource);
            }

            if (shouldRender)
            {
                RenderIntoEntry(drawable, resource, entry);
                entry.IsBoundsDirty = true;
            }

            RevalidateAll(entry.Node);
            var processor = new RenderNodeProcessor(
                _pool, entry.Node, CacheOptions.IsEnabled, RenderIntent, OutputScale, MaxWorkingScale, Diagnostics,
                PullPurpose)
            {
                RequestedBounds = new Rect(default, FrameSize.ToSize(1)),
            };
            var ops = processor.PullToRoot();
            Rect bounds = Rect.Empty;
            int consumed = 0;
            try
            {
                foreach (var op in ops)
                {
                    bounds = bounds.Union(op.Bounds);
                    op.Render(_immediateCanvas);
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
            // A frame pull is cropped to the viewport. Reuse its bounds only when they are strictly inside that crop;
            // touching an edge cannot distinguish exact content from clipped content, so preserve the dirty flag and let
            // the auxiliary full-bounds pull recover the off-screen extent. Empty output is ambiguous: it may be
            // genuinely empty or entirely outside the viewport, so only a full-bounds pull can make it exact.
            Rect requestedBounds = processor.RequestedBounds;
            entry.IsBoundsDirty = bounds.IsEmpty
                || (!requestedBounds.IsInvalid
                    && (bounds.Left <= requestedBounds.Left
                    || bounds.Top <= requestedBounds.Top
                    || bounds.Right >= requestedBounds.Right
                    || bounds.Bottom >= requestedBounds.Bottom));

            if (PullPurpose == RenderPullPurpose.Frame)
            {
                RenderNodeCacheHelper.MakeCache(
                    entry.Node, CacheOptions, RenderIntent, OutputScale, MaxWorkingScale, Diagnostics, _pool, PullPurpose);
            }
            return entry;
        }
        catch
        {
            if (entry != null)
            {
                EvictFaultedEntry(nodeCache, drawable, entry);
            }

            throw;
        }
    }

    private void AddDetachedHandler(Drawable drawable)
    {
        if (_detachedSubscriptions.TryGetValue(drawable, out _))
        {
            return;
        }

        var weakRef = new WeakReference<Renderer>(this);

        void Handler(object? sender, HierarchyAttachmentEventArgs e)
        {
            if (sender is not Drawable senderDrawable) return;

            Exception? failure = null;
            try
            {
                if (weakRef.TryGetTarget(out Renderer? renderer))
                {
                    renderer.DetachEntry(renderer._frameNodeCache, senderDrawable, ref failure);
                    renderer.DetachEntry(renderer._auxiliaryNodeCache, senderDrawable, ref failure);
                }
            }
            finally
            {
                senderDrawable.DetachedFromHierarchy -= Handler;
                if (weakRef.TryGetTarget(out Renderer? renderer))
                {
                    renderer._detachedSubscriptions.Remove(senderDrawable);
                }
            }

            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        _detachedSubscriptions.Add(drawable, new DetachedSubscription(Handler));
        drawable.DetachedFromHierarchy += Handler;
    }

    private void RenderIntoEntry(
        Drawable drawable,
        Drawable.Resource resource,
        Entry entry)
    {
        var context = new GraphicsContext2D(entry.Node, FrameSize.ToSize(1), OutputScale);
        Exception? failure = null;
        try
        {
            drawable.Render(context, resource);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        try
        {
            context.Dispose();
        }
        catch (Exception ex)
        {
            failure ??= ex;
        }

        if (failure != null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private void DetachAllHandlers()
    {
        KeyValuePair<Drawable, DetachedSubscription>[] subscriptions = [.. _detachedSubscriptions];
        _detachedSubscriptions.Clear();
        Exception? failure = null;
        foreach ((Drawable drawable, DetachedSubscription subscription) in subscriptions)
        {
            try
            {
                drawable.DetachedFromHierarchy -= subscription.Handler;
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }

        if (failure != null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static ConditionalWeakTable<Drawable, Entry> SelectNodeCache(
        ConditionalWeakTable<Drawable, Entry> frameCache,
        ConditionalWeakTable<Drawable, Entry> auxiliaryCache,
        RenderPullPurpose pullPurpose)
        => pullPurpose == RenderPullPurpose.Frame ? frameCache : auxiliaryCache;

    private ConditionalWeakTable<Drawable, Entry> GetNodeCache(RenderPullPurpose pullPurpose)
        => SelectNodeCache(_frameNodeCache, _auxiliaryNodeCache, pullPurpose);

    private void DetachEntry(
        ConditionalWeakTable<Drawable, Entry> cache,
        Drawable drawable,
        ref Exception? failure)
    {
        if (cache.TryGetValue(drawable, out Entry? entry))
        {
            cache.Remove(drawable);
            _allCurrentEntries.RemoveAll(current => ReferenceEquals(current, entry));
            try
            {
                RenderNodeCacheHelper.ClearCache(entry.Node);
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }

            try
            {
                entry.Dispose();
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }
    }

    private static void EvictFaultedEntry(
        ConditionalWeakTable<Drawable, Entry> cache,
        Drawable drawable,
        Entry entry)
    {
        if (cache.TryGetValue(drawable, out Entry? current) && ReferenceEquals(current, entry))
        {
            cache.Remove(drawable);
        }

        try
        {
            RenderNodeCacheHelper.ClearCache(entry.Node);
        }
        catch
        {
            // The render/update failure remains primary. Continue releasing the invalid tree.
        }

        try
        {
            entry.Dispose();
        }
        catch
        {
            // The render/update failure remains primary after the full best-effort cleanup.
        }
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
        ThrowIfDisposed();
        RenderThread.Dispatcher.VerifyAccess();
        RequireFrameIntent(frame, "frame update");
        try
        {
            Time = frame.Time.Start;
            ClearFrame();
            ConditionalWeakTable<Drawable, Entry> nodeCache = GetNodeCache(frame.PullPurpose);

            foreach (var obj in frame.Objects)
            {
                if (obj is not Drawable.Resource drawableResource)
                    continue;

                var drawable = drawableResource.GetOriginal();
                Entry? entry = null;
                try
                {
                    bool shouldRender;
                    if (!nodeCache.TryGetValue(drawable, out entry))
                    {
                        entry = new Entry(new DrawableRenderNode(drawableResource));
                        nodeCache.Add(drawable, entry);
                        AddDetachedHandler(drawable);
                        shouldRender = true;
                    }
                    else
                    {
                        shouldRender = entry.Node.Update(drawableResource);
                    }

                    if (shouldRender)
                    {
                        RenderIntoEntry(drawable, drawableResource, entry);
                        entry.IsBoundsDirty = true;
                    }

                    RevalidateAll(entry.Node);
                    _allCurrentEntries.Add(entry);
                }
                catch
                {
                    if (entry != null)
                    {
                        EvictFaultedEntry(nodeCache, drawable, entry);
                    }

                    throw;
                }
            }

            _currentFramePullPurpose = frame.PullPurpose;
            _hasCurrentFrame = true;
        }
        catch
        {
            _hasCurrentFrame = false;
            ClearFrame();
            throw;
        }
    }

    public Drawable? HitTest(CompositionFrame frame, Point point)
    {
        ThrowIfDisposed();
        RenderThread.Dispatcher.VerifyAccess();
        RequireFramePolicy(frame, RenderPullPurpose.Auxiliary, "hit testing");
        UpdateFrame(frame);

        for (int i = _allCurrentEntries.Count - 1; i >= 0; i--)
        {
            Entry entry = _allCurrentEntries[i];
            // Same scale pair as the render pass to avoid thrashing scale-stateful nodes.
            var processor = new RenderNodeProcessor(
                _pool, entry.Node, CacheOptions.IsEnabled, RenderIntent, OutputScale, MaxWorkingScale,
                diagnostics: null, pullPurpose: RenderPullPurpose.Auxiliary);
            var arr = processor.PullToRoot();
            Exception? operationFailure = null;
            try
            {
                if (arr.Any(op => op.HitTest(point)))
                {
                    return entry.Node.Drawable?.Resource.GetOriginal();
                }
            }
            catch (Exception ex)
            {
                operationFailure = ex;
                throw;
            }
            finally
            {
                Exception? cleanupFailure = null;
                RenderNodeOperation.DisposeAll(arr, ref cleanupFailure);
                if (operationFailure == null && cleanupFailure != null)
                {
                    ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
                }
            }
        }

        return null;
    }

    public Rect[] GetBoundaries(int zIndex)
    {
        ThrowIfDisposed();
        RenderThread.Dispatcher.VerifyAccess();
        return
        [
            .. _allCurrentEntries
                .Where(e => e.Node.Drawable?.Resource.GetOriginal().ZIndex == zIndex)
                .Select(EnsureBoundary)
        ];
    }

    /// <summary>
    /// Updates the render-node tree from an auxiliary composition frame before measuring layer boundaries.
    /// </summary>
    public Rect[] GetBoundaries(CompositionFrame frame, int zIndex)
    {
        ThrowIfDisposed();
        RequireFramePolicy(frame, RenderPullPurpose.Auxiliary, "boundary measurement");
        UpdateFrame(frame);
        return GetBoundaries(zIndex);
    }

    public Rect? GetBoundary(Drawable drawable)
    {
        ThrowIfDisposed();
        RenderThread.Dispatcher.VerifyAccess();
        Entry? currentEntry = _allCurrentEntries.FirstOrDefault(
            entry => ReferenceEquals(entry.Node.Drawable?.Resource.GetOriginal(), drawable));
        if (currentEntry != null)
        {
            return EnsureBoundary(currentEntry);
        }

        if (_frameNodeCache.TryGetValue(drawable, out _)
            || _auxiliaryNodeCache.TryGetValue(drawable, out _))
        {
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

    /// <summary>
    /// Updates the render-node tree from an auxiliary composition frame before measuring one drawable's boundary.
    /// </summary>
    public Rect? GetBoundary(CompositionFrame frame, Drawable drawable)
    {
        ThrowIfDisposed();
        RequireFramePolicy(frame, RenderPullPurpose.Auxiliary, "boundary measurement");
        UpdateFrame(frame);
        return GetBoundary(drawable);
    }

    public Rect[] RecalculateBoundaries(int zIndex)
    {
        ThrowIfDisposed();
        RenderThread.Dispatcher.VerifyAccess();
        return
        [
            .. _allCurrentEntries
                .Where(e => e.Node.Drawable?.Resource.GetOriginal().ZIndex == zIndex)
                .Select(EnsureBoundary)
        ];
    }

    /// <summary>
    /// Updates the render-node tree from an auxiliary composition frame before recalculating layer boundaries.
    /// </summary>
    public Rect[] RecalculateBoundaries(CompositionFrame frame, int zIndex)
    {
        ThrowIfDisposed();
        RequireFramePolicy(frame, RenderPullPurpose.Auxiliary, "boundary measurement");
        UpdateFrame(frame);
        return RecalculateBoundaries(zIndex);
    }

    private Rect EnsureBoundary(Entry entry)
    {
        return entry.IsBoundsDirty ? CalculateBoundary(entry) : entry.Bounds;
    }

    private Rect CalculateBoundary(Entry entry)
    {
        if (!_hasCurrentFrame || _currentFramePullPurpose != RenderPullPurpose.Auxiliary)
        {
            throw new InvalidOperationException(
                "Dirty boundary calculation requires an auxiliary composition frame. "
                + "Evaluate the compositor with RenderPullPurpose.Auxiliary and use the frame-taking boundary overload.");
        }

        var processor = new RenderNodeProcessor(
            _pool, entry.Node, CacheOptions.IsEnabled, RenderIntent, OutputScale, MaxWorkingScale,
            diagnostics: null, pullPurpose: RenderPullPurpose.Auxiliary);
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

        entry.Bounds = bounds;
        entry.IsBoundsDirty = false;
        return bounds;
    }

    private void RequireFramePolicy(
        CompositionFrame frame,
        RenderPullPurpose expectedPurpose,
        string operation)
    {
        RequireFrameIntent(frame, operation);
        if (frame.PullPurpose != expectedPurpose)
        {
            throw new ArgumentException(
                $"Renderer {operation} requires a {expectedPurpose} composition frame, but received {frame.PullPurpose}.",
                nameof(frame));
        }
    }

    private void RequireFrameIntent(CompositionFrame frame, string operation)
    {
        if (frame.RenderIntent != RenderIntent)
        {
            throw new ArgumentException(
                $"Renderer {operation} requires a {RenderIntent} composition frame, but received {frame.RenderIntent}.",
                nameof(frame));
        }
    }

    public DrawableRenderNode? FindRenderNode(Drawable drawable)
    {
        ThrowIfDisposed();
        Entry? currentEntry = _allCurrentEntries.FirstOrDefault(
            item => ReferenceEquals(item.Node.Drawable?.Resource.GetOriginal(), drawable));
        if (currentEntry != null)
        {
            return currentEntry.Node;
        }

        foreach (Entry entry in _allCurrentEntries)
        {
            if (entry.Node is ContainerRenderNode currentContainer
                && FindChildRenderNode(currentContainer, drawable) is { } currentChild)
            {
                return currentChild;
            }
        }

        return null;
    }

    private static DrawableRenderNode? FindChildRenderNode(ContainerRenderNode container, Drawable drawable)
    {
        foreach (var child in container.Children)
        {
            if (child is DrawableRenderNode childDrawable
                && ReferenceEquals(childDrawable.Drawable?.Resource.GetOriginal(), drawable))
            {
                return childDrawable;
            }

            if (child is ContainerRenderNode childContainer)
            {
                var result = FindChildRenderNode(childContainer, drawable);
                if (result != null)
                    return result;
            }
        }

        return null;
    }

    public Bitmap Snapshot()
    {
        ThrowIfDisposed();
        RenderThread.Dispatcher.VerifyAccess();
        return _surface.Snapshot();
    }

    /// <summary>
    /// Reads the current surface into an existing <paramref name="destination"/> bitmap, reusing it
    /// instead of allocating a fresh snapshot. See <see cref="RenderTarget.SnapshotInto(Bitmap)"/>.
    /// </summary>
    public void SnapshotInto(Bitmap destination)
    {
        ThrowIfDisposed();
        RenderThread.Dispatcher.VerifyAccess();
        _surface.SnapshotInto(destination);
    }

    /// <summary>
    /// Allocates a bitmap in the format <see cref="Snapshot()"/> produces, suitable as a reusable
    /// destination for <see cref="SnapshotInto(Bitmap)"/>. See <see cref="RenderTarget.CreateSnapshotBitmap()"/>.
    /// </summary>
    public Bitmap CreateSnapshotBitmap()
    {
        ThrowIfDisposed();
        return _surface.CreateSnapshotBitmap();
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(IsDisposed, this);

    public void ClearAllCaches()
    {
        _hasCurrentFrame = false;
        ClearFrame();
        Entry[] entries =
        [
            .. _frameNodeCache.Select(static item => item.Value),
            .. _auxiliaryNodeCache.Select(static item => item.Value),
        ];
        _frameNodeCache.Clear();
        _auxiliaryNodeCache.Clear();
        Exception? failure = null;
        foreach (Entry entry in entries)
        {
            try
            {
                RenderNodeCacheHelper.ClearCache(entry.Node);
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }

            try
            {
                entry.Dispose();
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }

        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private void DisposeAllEntries()
    {
        Entry[] entries =
        [
            .. _frameNodeCache.Select(static item => item.Value),
            .. _auxiliaryNodeCache.Select(static item => item.Value),
        ];
        _frameNodeCache.Clear();
        _auxiliaryNodeCache.Clear();
        Exception? failure = null;
        foreach (Entry entry in entries)
        {
            // Compositor側でDisposeされるのでResourceはDisposeせず、NodeだけがDisposeされるようにする
            try
            {
                entry.Dispose();
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }

        if (failure != null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
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
