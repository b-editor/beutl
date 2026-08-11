using System.Runtime.CompilerServices;
using Beutl.Composition;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Threading;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Rendering;

public class Renderer : IRenderer
{
    private static readonly ILogger s_logger = Log.CreateLogger<Renderer>();

    private readonly ImmediateCanvas _immediateCanvas;
    private readonly RenderTarget _surface;
    private readonly Dispatcher _dispatcher;
    private readonly ConditionalWeakTable<Drawable, Entry> _nodeCache = new();
    private readonly List<Entry> _allCurrentEntries = [];

    // One revalidation pass, not one entry: a referenced subtree is reachable from several entries
    // and the cache-admission threshold counts one visit per frame.
    private readonly HashSet<RenderNode> _revalidatedNodes = new(ReferenceEqualityComparer.Instance);

    private readonly ClearRenderNode _frameClear;
    private readonly CompleteTargetRenderNode _completeTarget;
    private RenderNodeRenderer _frameRenderer;
    private RenderCacheOptions _cacheOptions = RenderCacheOptions.CreateFromGlobalConfiguration();

    private class Entry(DrawableRenderNode node, RenderNodeRenderer renderer, Dispatcher dispatcher) : IDisposable
    {
        private Rect _bounds;

        ~Entry()
        {
            GpuResourceRelease.DispatchFinalizer(
                dispatcher,
                () =>
                {
                    Dispose();
                });
        }

        public DrawableRenderNode Node { get; } = node;

        public RenderNodeRenderer Renderer { get; } = renderer;

        public bool IsDisposed { get; private set; }

        public void InvalidateBounds()
        {
            _bounds = default;
            HasValidBounds = false;
        }

        public Rect GetBounds()
        {
            if (!HasValidBounds)
            {
                _bounds = Renderer.Measure().QueryBounds;
                HasValidBounds = true;
            }

            return _bounds;
        }

        public Rect RecalculateBounds()
        {
            _bounds = Renderer.Measure().QueryBounds;
            HasValidBounds = true;
            return _bounds;
        }

        private bool HasValidBounds { get; set; }

        public void Dispose()
        {
            VerifyCleanupAccess(dispatcher);
            if (!IsDisposed)
            {
                IsDisposed = true;
                Exception? primary = null;
                try
                {
                    Renderer.Dispose();
                }
                catch (Exception ex)
                {
                    primary = ex;
                }

                try
                {
                    Node.Dispose();
                }
                catch (Exception ex)
                {
                    primary ??= ex;
                }

                GC.SuppressFinalize(this);
                if (primary is not null)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
            }
        }
    }

    public Renderer(
        int width,
        int height,
        float renderScale = 1f,
        float maxWorkingScale = float.PositiveInfinity,
        RenderIntent intent = RenderIntent.Preview)
        : this(
            width,
            height,
            renderScale,
            maxWorkingScale,
            surface: null,
            intent: intent)
    {
    }

    internal Renderer(
        int width,
        int height,
        float renderScale,
        float maxWorkingScale,
        RenderTarget? surface,
        RenderIntent intent = RenderIntent.Preview,
        Dispatcher? dispatcher = null)
    {
        static void DisposePreservingPrimaryFailure(IDisposable? value)
        {
            try
            {
                value?.Dispose();
            }
            catch
            {
                // Constructor cleanup must not replace the failure that triggered it.
            }
        }

        _dispatcher = dispatcher ?? RenderThread.Dispatcher;

        if (!Enum.IsDefined(intent))
        {
            // This constructor owns `surface` from entry.
            DisposePreservingPrimaryFailure(surface);
            throw new ArgumentOutOfRangeException(nameof(intent), intent, "Unknown render intent.");
        }

        float outputScale = float.IsFinite(renderScale) && renderScale > 0f ? renderScale : 1f;
        float maxScale = RenderScaleUtilities.SanitizeMaxWorkingScale(maxWorkingScale);
        FrameSize = new PixelSize(width, height);
        OutputScale = outputScale;
        MaxWorkingScale = maxScale;
        Intent = intent;
        DeviceSize = new PixelSize(
            (int)MathF.Ceiling(width * outputScale),
            (int)MathF.Ceiling(height * outputScale));
        _frameClear = new ClearRenderNode(default);
        _completeTarget = new CompleteTargetRenderNode(_frameClear, []);
        _frameRenderer = CreateEntryRenderer(
            _completeTarget,
            RenderRequestPurpose.Frame);
        try
        {
            (_immediateCanvas, _surface) = _dispatcher.Invoke(() =>
            {
                RenderTarget? actualSurface = null;
                try
                {
                    actualSurface = surface
                        ?? RenderTarget.Create(DeviceSize.Width, DeviceSize.Height)
                        ?? throw new InvalidOperationException(
                            $"Could not create a canvas of this size. (width: {DeviceSize.Width}, height: {DeviceSize.Height})");
                    if (actualSurface.Width != DeviceSize.Width || actualSurface.Height != DeviceSize.Height)
                    {
                        throw new ArgumentException(
                            "The injected render target must match the renderer device size.",
                            nameof(surface));
                    }

                    var canvas = new ImmediateCanvas(actualSurface, outputScale, maxScale,
                        logicalSize: FrameSize.ToSize(1), intent: intent);
                    return (canvas, actualSurface);
                }
                catch
                {
                    DisposePreservingPrimaryFailure(actualSurface);
                    throw;
                }
            });
        }
        catch
        {
            // Construction transferred ownership of these helpers before the surface was created.
            // Release all of them, but never replace the constructor's primary failure.
            DisposePreservingPrimaryFailure(_frameRenderer);
            DisposePreservingPrimaryFailure(_completeTarget);
            DisposePreservingPrimaryFailure(_frameClear);

            throw;
        }
    }

    ~Renderer()
    {
        // A finalizer must never throw or release render-owned resources from the finalizer thread.
        if (Interlocked.CompareExchange(ref _disposeClaimed, 1, 0) != 0)
            return;

        try
        {
            OnDispose(false);
        }
        catch (Exception ex)
        {
            s_logger.LogDebug(ex, "Renderer finalizer: OnDispose threw during last-resort disposal");
        }

        try
        {
            DispatchFinalizerRenderResourceCleanup();
        }
        catch (Exception ex)
        {
            s_logger.LogDebug(ex, "Renderer finalizer: cleanup dispatch threw during last-resort disposal");
        }
    }

    private int _disposeClaimed;

    public bool IsDisposed => Volatile.Read(ref _disposeClaimed) != 0;

    public bool IsGraphicsRendering { get; private set; }

    public RenderCacheOptions CacheOptions
    {
        get => _cacheOptions;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            GpuResourceRelease.RunRequired(_dispatcher, () => SetCacheOptionsCore(value));
        }
    }

    public TimeSpan Time { get; internal set; }

    public PixelSize FrameSize { get; }

    /// <summary>Output scale <c>s_out</c> (device px per logical unit). <see cref="FrameSize"/> stays logical.</summary>
    public float OutputScale { get; }

    /// <summary>Working-scale ceiling. Preview: <c>2 * s_out</c>; export: <c>+Inf</c>.</summary>
    public float MaxWorkingScale { get; }

    /// <summary>
    /// Intent applied to every request this renderer issues. <see cref="RenderIntent.Preview"/> drops a
    /// contribution whose intermediate target cannot be allocated; <see cref="RenderIntent.Delivery"/>
    /// fails the render instead, so a delivery-grade output never silently loses content.
    /// </summary>
    public RenderIntent Intent { get; }

    /// <summary>
    /// The physical backing-surface size, <c>ceil(FrameSize × OutputScale)</c>.
    /// Ceiling preserves fractional edge pixels; only place OutputScale sizes a surface.
    /// </summary>
    public PixelSize DeviceSize { get; }

    internal StructuralPlanCacheStatistics FrameStructuralPlanCacheStatistics
        => _frameRenderer.StructuralPlanCacheStatistics;

    internal ProgramCacheStatistics FrameProgramCacheStatistics
        => _frameRenderer.ProgramCacheStatistics;

    internal RenderTargetPoolStatistics FrameTargetPoolStatistics
        => _frameRenderer.TargetPoolStatistics;

    internal long RetainedRenderTargetBytes
        => _frameRenderer.TargetPoolStatistics.RetainedBytes
           + _nodeCache.Sum(static pair => pair.Value.Renderer.TargetPoolStatistics.RetainedBytes);

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposeClaimed, 1, 0) != 0)
            return;

        Exception? primary = null;

        CaptureCleanupFailure(() => OnDispose(true), ref primary);
        CaptureCleanupFailure(
            () => GpuResourceRelease.Run(_dispatcher, () =>
            {
                Exception? renderResourceFailure = DisposeRenderResourcesCore();
                if (renderResourceFailure is not null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo
                        .Capture(renderResourceFailure)
                        .Throw();
                }
            }),
            ref primary);
        GC.SuppressFinalize(this);

        if (primary is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private static void CaptureCleanupFailure(Action action, ref Exception? primary)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            primary ??= ex;
        }
    }

    private static void VerifyCleanupAccess(Dispatcher dispatcher)
    {
        if (!dispatcher.CheckAccess() && !dispatcher.HasShutdownFinished)
        {
            dispatcher.VerifyAccess();
        }
    }

    private Exception? DisposeRenderResourcesCore()
    {
        VerifyCleanupAccess(_dispatcher);
        Exception? primary = null;
        CaptureCleanupFailure(() => _completeTarget?.UpdateRoots([]), ref primary);
        CaptureCleanupFailure(() => _frameRenderer?.Dispose(), ref primary);
        CaptureCleanupFailure(() => _completeTarget?.Dispose(), ref primary);
        CaptureCleanupFailure(() => _frameClear?.Dispose(), ref primary);
        CaptureCleanupFailure(() => _immediateCanvas?.Dispose(), ref primary);
        CaptureCleanupFailure(() => _surface?.Dispose(), ref primary);
        CaptureCleanupFailure(ClearEntryCachesCore, ref primary);
        CaptureCleanupFailure(DisposeAllEntriesCore, ref primary);
        return primary;
    }

    /// <summary>Releases resources owned by a derived renderer.</summary>
    /// <param name="disposing">
    /// <c>true</c> when called synchronously by <see cref="Dispose"/>; <c>false</c> when called by the finalizer.
    /// </param>
    /// <remarks>
    /// <see cref="IsDisposed"/> is already <c>true</c> when this method is called. The <c>true</c> path runs
    /// inline and synchronously on the thread calling <see cref="Dispose"/>. The <c>false</c> path runs inline
    /// on the finalizer thread before render-resource cleanup is dispatched, and must not access
    /// render-thread-affine resources.
    /// </remarks>
    protected virtual void OnDispose(bool disposing)
    {
    }

    public void Render(CompositionFrame frame)
    {
        _dispatcher.VerifyAccess();
        if (IsGraphicsRendering)
            return;

        try
        {
            IsGraphicsRendering = true;
            Time = frame.Time.Start;
            using (_immediateCanvas.Push())
            {
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
        var pendingEntries = new List<Entry>();
        try
        {
            PrepareEntries(frame, pendingEntries);

            _completeTarget.UpdateRoots(pendingEntries.Select(static entry => (RenderNode)entry.Node));
            _frameRenderer.Render(_immediateCanvas);
        }
        finally
        {
            ClearFrame();
        }

        RevalidateEntries(pendingEntries);
        _allCurrentEntries.AddRange(pendingEntries);
    }

    private void PrepareEntries(CompositionFrame frame, List<Entry> destination)
    {
        foreach (var obj in frame.Objects)
        {
            if (obj is not Drawable.Resource drawableResource)
                continue;

            destination.Add(PrepareDrawable(drawableResource));
        }
    }

    // A mark on a shared node may only be consumed once every entry that reads it this frame has read it.
    // Preparation therefore completes before any entry is revalidated, and a frame that faults during
    // preparation revalidates nothing: the entries the fault skipped still owe those marks a read.
    private void RevalidateEntries(List<Entry> entries)
    {
        try
        {
            foreach (Entry entry in entries)
            {
                RevalidateAll(entry.Node);
                entry.InvalidateBounds();
            }
        }
        finally
        {
            _revalidatedNodes.Clear();
        }
    }

    private Entry PrepareDrawable(Drawable.Resource resource)
    {
        Drawable drawable = resource.GetOriginal();
        Entry entry;
        bool shouldRender;

        if (!_nodeCache.TryGetValue(drawable, out entry!))
        {
            AddDetachedHandler(drawable);
            entry = CreateEntry(resource);
            _nodeCache.Add(drawable, entry);
            shouldRender = true;
        }
        else
        {
            shouldRender = entry.Node.Update(resource) || entry.Node.HasChanges;
        }

        if (shouldRender)
        {
            try
            {
                using var ctx = new GraphicsContext2D(entry.Node, FrameSize.ToSize(1), OutputScale);
                drawable.Render(ctx, resource);
            }
            catch
            {
                entry.Node.HasChanges = true;
                throw;
            }
        }

        return entry;
    }

    private Entry CreateEntry(Drawable.Resource resource)
    {
        var node = new DrawableRenderNode(resource);
        try
        {
            return new Entry(node, CreateEntryRenderer(node), _dispatcher);
        }
        catch
        {
            node.Dispose();
            throw;
        }
    }

    private RenderNodeRenderer CreateEntryRenderer(
        RenderNode node,
        RenderRequestPurpose purpose = RenderRequestPurpose.Auxiliary,
        RenderCacheOptions? cacheOptions = null)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = Intent,
                    TargetDomain = new Rect(default, FrameSize.ToSize(1)),
                    OutputScale = OutputScale,
                    MaxWorkingScale = MaxWorkingScale,
                    CacheOptions = cacheOptions ?? CacheOptions,
                    Purpose = purpose,
                },
            });

    private void AddDetachedHandler(Drawable drawable)
    {
        var weakRef = new WeakReference<Renderer>(this);
        Dispatcher dispatcher = _dispatcher;

        void Handler(object? sender, HierarchyAttachmentEventArgs e)
        {
            if (sender is not Drawable senderDrawable) return;

            senderDrawable.DetachedFromHierarchy -= Handler;
            if (dispatcher.HasShutdownStarted)
            {
                return;
            }

            var drawableRef = new WeakReference<Drawable>(senderDrawable);

            // Detaching happens on the edit thread, but the entry's cache is GPU state owned by the
            // render thread. Queued rather than awaited so an edit never blocks behind a frame.
            dispatcher.Dispatch(() =>
            {
                if (!weakRef.TryGetTarget(out Renderer? renderer)
                    || !drawableRef.TryGetTarget(out Drawable? detachedDrawable))
                {
                    return;
                }

                try
                {
                    renderer.EvictEntryCore(detachedDrawable);
                }
                catch (Exception ex)
                {
                    s_logger.LogWarning(ex, "Failed to dispose a detached drawable's render entry");
                }
            });
        }

        drawable.DetachedFromHierarchy += Handler;
    }

    private void EvictEntryCore(Drawable drawable)
    {
        _dispatcher.VerifyAccess();
        if (_nodeCache.TryGetValue(drawable, out Entry? entry))
        {
            _nodeCache.Remove(drawable);
            DisposeEntryCore(entry, clearCache: true);
        }
    }

    private void RevalidateAll(RenderNode current)
    {
        if (current.IsDisposed || !_revalidatedNodes.Add(current))
            return;

        ReadOnlySpan<RenderNode> children = current.ChildNodes;
        for (int i = 0; i < children.Length; i++)
        {
            RevalidateAll(children[i]);
        }

        RenderNodeCache cache = current.Cache;
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
        _dispatcher.VerifyAccess();
        Time = frame.Time.Start;
        ClearFrame();
        var pendingEntries = new List<Entry>();

        PrepareEntries(frame, pendingEntries);
        RevalidateEntries(pendingEntries);
        _allCurrentEntries.AddRange(pendingEntries);
    }

    public Drawable? HitTest(CompositionFrame frame, Point point)
    {
        _dispatcher.VerifyAccess();
        UpdateFrame(frame);

        for (int i = _allCurrentEntries.Count - 1; i >= 0; i--)
        {
            Entry entry = _allCurrentEntries[i];
            // Same scale pair as the render pass to avoid thrashing scale-stateful nodes.
            if (entry.Renderer.HitTest(point))
            {
                return entry.Node.Drawable?.Resource.GetOriginal();
            }
        }

        return null;
    }

    public Rect[] GetBoundaries(int zIndex)
    {
        _dispatcher.VerifyAccess();
        return [.. _allCurrentEntries
            .Where(e => e.Node.Drawable?.Resource.GetOriginal().ZIndex == zIndex)
            .Select(e => e.GetBounds())];
    }

    public Rect? GetBoundary(Drawable drawable)
    {
        _dispatcher.VerifyAccess();
        if (_nodeCache.TryGetValue(drawable, out Entry? entry))
        {
            if (_allCurrentEntries.Contains(entry))
            {
                return entry.GetBounds();
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

    /// <summary>Recalculates and caches current-frame bounds for drawables at the specified z-index.</summary>
    /// <remarks>This method must be called on the render thread.</remarks>
    /// <exception cref="InvalidOperationException">The caller does not have render-thread access.</exception>
    public Rect[] RecalculateBoundaries(int zIndex)
    {
        _dispatcher.VerifyAccess();
        return [.. _allCurrentEntries
            .Where(e => e.Node.Drawable?.Resource.GetOriginal().ZIndex == zIndex)
            .Select(e => e.RecalculateBounds())];
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
        _dispatcher.VerifyAccess();
        return _surface.Snapshot();
    }

    /// <summary>
    /// Reads the current surface into an existing <paramref name="destination"/> bitmap, reusing it
    /// instead of allocating a fresh snapshot. See <see cref="RenderTarget.SnapshotInto(Bitmap)"/>.
    /// </summary>
    public void SnapshotInto(Bitmap destination)
    {
        _dispatcher.VerifyAccess();
        _surface.SnapshotInto(destination);
    }

    /// <summary>
    /// Allocates a bitmap in the format <see cref="Snapshot()"/> produces, suitable as a reusable
    /// destination for <see cref="SnapshotInto(Bitmap)"/>. See <see cref="RenderTarget.CreateSnapshotBitmap()"/>.
    /// </summary>
    public Bitmap CreateSnapshotBitmap() => _surface.CreateSnapshotBitmap();

    /// <summary>
    /// Releases reusable intermediate render targets retained by this renderer.
    /// </summary>
    /// <returns>The number of pooled target bytes released.</returns>
    /// <remarks>
    /// Call this between frames on long-running delivery renders to bound backend memory. The current
    /// output surface and compiled render plans remain intact, so the next frame only recreates the
    /// intermediate targets it needs.
    /// </remarks>
    public long ReleaseRetainedRenderTargets()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return GpuResourceRelease.RunRequired(_dispatcher, ReleaseRetainedRenderTargetsCore);
    }

    private long ReleaseRetainedRenderTargetsCore()
    {
        _dispatcher.VerifyAccess();
        long released = _frameRenderer.ReleaseRetainedTargets();
        foreach (KeyValuePair<Drawable, Entry> pair in _nodeCache)
        {
            released = checked(released + pair.Value.Renderer.ReleaseRetainedTargets());
        }

        if (released > 0 && GraphicsContextFactory.SharedContext is { } context)
        {
            // Disposing an SKSurface only unlocks its backing allocation; Ganesh may retain that allocation
            // in the shared resource cache. Submit completed work before purging the released scratch bytes
            // so Metal/Vulkan can actually return them instead of growing once per delivery frame.
            context.SkiaContext.Flush(submit: true, synchronous: true);
            GpuResourceReclaimQueue.DrainAfterContextSync();
            context.SkiaContext.PurgeUnlockedResources(released, preferScratchResources: true);
        }

        return released;
    }

    public void ClearAllCaches()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        GpuResourceRelease.RunRequired(_dispatcher, ClearAllCachesCore);
    }

    private void SetCacheOptionsCore(RenderCacheOptions value)
    {
        ResetAllCachesCore(value, updateCacheOptions: true);
    }

    private void ClearAllCachesCore()
    {
        ResetAllCachesCore(_cacheOptions, updateCacheOptions: false);
    }

    private void ResetAllCachesCore(RenderCacheOptions cacheOptions, bool updateCacheOptions)
    {
        _dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        Exception? primary = null;
        CaptureCleanupFailure(() => _completeTarget.UpdateRoots([]), ref primary);

        RenderNodeRenderer? replacement = null;
        try
        {
            replacement = CreateEntryRenderer(
                _completeTarget,
                RenderRequestPurpose.Frame,
                cacheOptions);
        }
        catch (Exception ex)
        {
            primary ??= ex;
        }

        if (replacement is not null)
        {
            RenderNodeRenderer previous = _frameRenderer;
            _frameRenderer = replacement;
            if (updateCacheOptions)
                _cacheOptions = cacheOptions;
            CaptureCleanupFailure(previous.Dispose, ref primary);
        }

        CaptureCleanupFailure(ClearEntryCachesCore, ref primary);

        if (primary is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private void ClearEntryCachesCore()
    {
        VerifyCleanupAccess(_dispatcher);
        var entries = _nodeCache?.ToArray() ?? [];
        _nodeCache?.Clear();
        _allCurrentEntries?.Clear();
        Exception? primary = null;
        foreach (var item in entries)
        {
            try
            {
                DisposeEntryCore(item.Value, clearCache: true);
            }
            catch (Exception ex)
            {
                primary ??= ex;
            }
        }

        if (primary is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private void DisposeAllEntriesCore()
    {
        VerifyCleanupAccess(_dispatcher);
        var entries = _nodeCache?.ToArray() ?? [];
        _nodeCache?.Clear();
        Exception? primary = null;
        foreach (var item in entries)
        {
            // Compositor側でDisposeされるのでResourceはDisposeせず、NodeだけがDisposeされるようにする
            try
            {
                DisposeEntryCore(item.Value, clearCache: false);
            }
            catch (Exception ex)
            {
                primary ??= ex;
            }
        }

        if (primary is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private void DisposeEntryCore(Entry entry, bool clearCache)
    {
        VerifyCleanupAccess(_dispatcher);
        Exception? primary = null;
        if (clearCache)
        {
            try
            {
                RenderNodeCacheHelper.ClearCache(entry.Node);
            }
            catch (Exception ex)
            {
                primary = ex;
            }
        }

        try
        {
            entry.Dispose();
        }
        catch (Exception ex)
        {
            primary ??= ex;
        }

        if (primary is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private void DispatchFinalizerRenderResourceCleanup()
    {
        GpuResourceRelease.DispatchFinalizer(_dispatcher, () =>
        {
            Exception? primary = DisposeRenderResourcesCore();
            if (primary is not null)
            {
                s_logger.LogDebug(
                    primary,
                    "Renderer finalizer: render resource cleanup threw during last-resort disposal");
            }
        });
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

/// <summary>
/// Records the complete ordered set of roots for one target before any of them execute. The roots remain
/// externally owned; this request-local facade never retains fragment handles or disposes render nodes.
/// </summary>
internal sealed class CompleteTargetRenderNode : RenderNode
{
    private readonly RenderNode _first;

    // Replaced rather than mutated, so a span handed out by ChildNodes survives an UpdateRoots mid-traversal.
    private RenderNode[] _roots;

    public CompleteTargetRenderNode(RenderNode first, IEnumerable<RenderNode> remaining)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(remaining);
        _first = first;
        _roots = [first, .. remaining];
        if (_roots.Any(static root => root is null))
            throw new ArgumentException("A complete-target root sequence cannot contain null nodes.", nameof(remaining));
    }

    public void UpdateRoots(IEnumerable<RenderNode> remaining)
    {
        ArgumentNullException.ThrowIfNull(remaining);
        RenderNode[] roots = [_first, .. remaining];
        if (roots.Any(static root => root is null))
            throw new ArgumentException("A complete-target root sequence cannot contain null nodes.", nameof(remaining));
        _roots = roots;
    }

    public override ReadOnlySpan<RenderNode> ChildNodes => _roots;

    public override void Process(RenderNodeContext context)
    {
        foreach (RenderNode root in _roots)
            context.PublishRange(context.RecordSubtree(root));
    }
}
