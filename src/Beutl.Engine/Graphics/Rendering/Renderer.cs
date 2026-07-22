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
    private readonly IRenderPipelineDiagnosticsState? _diagnostics;
    private readonly ConditionalWeakTable<Drawable, Entry> _nodeCache = new();
    private readonly List<Entry> _allCurrentEntries = [];
    private readonly ClearRenderNode _frameClear;
    private readonly CompleteTargetRenderNode _completeTarget;
    private RenderNodeRenderer _frameRenderer;
    private RenderCacheOptions _cacheOptions = RenderCacheOptions.CreateFromGlobalConfiguration();

    private class Entry(DrawableRenderNode node, RenderNodeRenderer renderer) : IDisposable
    {
        ~Entry()
        {
            try
            {
                Dispose();
            }
            catch
            {
                // Finalizers cannot surface renderer or node cleanup failures.
            }
        }

        public DrawableRenderNode Node { get; } = node;

        public RenderNodeRenderer Renderer { get; } = renderer;

        public Rect Bounds { get; set; }

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
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

    public Renderer(int width, int height, float renderScale = 1f, float maxWorkingScale = float.PositiveInfinity)
        : this(
            width,
            height,
            renderScale,
            maxWorkingScale,
            new RenderPipelineDiagnosticsState(),
            surface: null)
    {
    }

    internal Renderer(
        int width,
        int height,
        float renderScale,
        float maxWorkingScale,
        IRenderPipelineDiagnosticsState? diagnostics,
        RenderTarget? surface)
    {
        float outputScale = float.IsFinite(renderScale) && renderScale > 0f ? renderScale : 1f;
        float maxScale = RenderScaleUtilities.SanitizeMaxWorkingScale(maxWorkingScale);
        FrameSize = new PixelSize(width, height);
        OutputScale = outputScale;
        MaxWorkingScale = maxScale;
        DeviceSize = new PixelSize(
            (int)MathF.Ceiling(width * outputScale),
            (int)MathF.Ceiling(height * outputScale));
        _diagnostics = diagnostics;
        _frameClear = new ClearRenderNode(default);
        _completeTarget = new CompleteTargetRenderNode(_frameClear, []);
        _frameRenderer = CreateEntryRenderer(
            _completeTarget,
            RenderRequestPurpose.Frame,
            _diagnostics);
        try
        {
            (_immediateCanvas, _surface) = RenderThread.Dispatcher.Invoke(() =>
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
                        logicalSize: FrameSize.ToSize(1));
                    return (canvas, actualSurface);
                }
                catch
                {
                    try
                    {
                        actualSurface?.Dispose();
                    }
                    catch
                    {
                        // Preserve the constructor failure while completing the ownership transfer.
                    }

                    throw;
                }
            });
        }
        catch
        {
            // Construction transferred ownership of these helpers before the surface was created.
            // Release all of them, but never replace the constructor's primary failure.
            try
            {
                _frameRenderer.Dispose();
            }
            catch
            {
            }

            try
            {
                _completeTarget.Dispose();
            }
            catch
            {
            }

            try
            {
                _frameClear.Dispose();
            }
            catch
            {
            }

            throw;
        }
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

        _isDisposed = true;
        SafeStep(nameof(OnDispose), () => OnDispose(false));
        SafeStep(nameof(_frameRenderer), () => _frameRenderer?.Dispose());
        SafeStep(nameof(_completeTarget), () => _completeTarget?.Dispose());
        SafeStep(nameof(_frameClear), () => _frameClear?.Dispose());
        SafeStep(nameof(_immediateCanvas), () => _immediateCanvas?.Dispose());
        SafeStep(nameof(_surface), () => _surface?.Dispose());
        SafeStep(nameof(ClearAllCaches), ClearAllCaches);
        SafeStep(nameof(DisposeAllEntries), DisposeAllEntries);
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
            RenderNodeRenderer replacement = CreateEntryRenderer(
                _completeTarget,
                RenderRequestPurpose.Frame,
                _diagnostics);
            RenderNodeRenderer previous = _frameRenderer;
            _frameRenderer = replacement;
            previous.Dispose();
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

    internal StructuralPlanCacheStatistics FrameStructuralPlanCacheStatistics
        => _frameRenderer.StructuralPlanCacheStatistics;

    internal ProgramCacheStatistics FrameProgramCacheStatistics
        => _frameRenderer.ProgramCacheStatistics;

    internal RenderTargetPoolStatistics FrameTargetPoolStatistics
        => _frameRenderer.TargetPoolStatistics;

    public void Dispose()
    {
        if (!IsDisposed)
        {
            _isDisposed = true;
            Exception? primary = null;

            void DisposeStep(Action action)
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

            DisposeStep(() => OnDispose(true));
            DisposeStep(_frameRenderer.Dispose);
            DisposeStep(_completeTarget.Dispose);
            DisposeStep(_frameClear.Dispose);
            DisposeStep(_immediateCanvas.Dispose);
            DisposeStep(_surface.Dispose);
            DisposeStep(ClearAllCaches);
            DisposeStep(DisposeAllEntries);
            GC.SuppressFinalize(this);

            if (primary is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
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
        foreach (var obj in frame.Objects)
        {
            if (obj is not Drawable.Resource drawableResource)
                continue;

            pendingEntries.Add(PrepareDrawable(drawableResource));
        }

        var pendingBounds = new Rect[pendingEntries.Count];
        for (int index = 0; index < pendingEntries.Count; index++)
        {
            pendingBounds[index] = pendingEntries[index].Renderer.Measure().OutputBounds;
        }

        _completeTarget.UpdateRoots(pendingEntries.Select(static entry => (RenderNode)entry.Node));
        _frameRenderer.Render(_immediateCanvas);

        ClearFrame();
        for (int index = 0; index < pendingEntries.Count; index++)
        {
            Entry entry = pendingEntries[index];
            entry.Bounds = pendingBounds[index];
            RevalidateAll(entry.Node);
            _allCurrentEntries.Add(entry);
        }
    }

    private Entry PrepareDrawable(Drawable.Resource resource)
    {
        var drawable = resource.GetOriginal();
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
            shouldRender = entry.Node.Update(resource);
        }

        if (shouldRender)
        {
            using var ctx = new GraphicsContext2D(entry.Node, FrameSize.ToSize(1), OutputScale);
            drawable.Render(ctx, resource);
        }

        return entry;
    }

    private Entry CreateEntry(Drawable.Resource resource)
    {
        var node = new DrawableRenderNode(resource);
        try
        {
            return new Entry(node, CreateEntryRenderer(node));
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
        IRenderPipelineDiagnosticsState? diagnostics = null)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                Intent = RenderIntent.Preview,
                TargetDomain = new Rect(default, FrameSize.ToSize(1)),
                OutputScale = OutputScale,
                MaxWorkingScale = MaxWorkingScale,
                UseRenderCache = CacheOptions.IsEnabled,
                CacheRules = CacheOptions.Rules,
                RenderPurpose = purpose,
                Diagnostics = diagnostics,
            });

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
                entry = CreateEntry(drawableResource);
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
            if (entry.Renderer.HitTest(point))
            {
                return entry.Node.Drawable?.Resource.GetOriginal();
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
            Rect bounds = e.Renderer.Measure().OutputBounds;
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

    public void ClearAllCaches()
    {
        var entries = _nodeCache.ToArray();
        _nodeCache.Clear();
        Exception? primary = null;
        foreach (var item in entries)
        {
            try
            {
                RenderNodeCacheHelper.ClearCache(item.Value.Node);
            }
            catch (Exception ex)
            {
                primary ??= ex;
            }

            try
            {
                item.Value.Dispose();
            }
            catch (Exception ex)
            {
                primary ??= ex;
            }
        }

        if (primary is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private void DisposeAllEntries()
    {
        Exception? primary = null;
        foreach (var item in _nodeCache)
        {
            // Compositor側でDisposeされるのでResourceはDisposeせず、NodeだけがDisposeされるようにする
            try
            {
                item.Value.Dispose();
            }
            catch (Exception ex)
            {
                primary ??= ex;
            }
        }
        _nodeCache.Clear();

        if (primary is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
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

    public override void Process(RenderNodeContext context)
    {
        foreach (RenderNode root in _roots)
            context.PublishRange(context.RecordSubtree(root));
    }
}
