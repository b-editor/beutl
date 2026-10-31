using System.Runtime.CompilerServices;
using Beutl.Composition;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
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
    private bool _frameContainsScene3D;

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
            => HasValidBounds ? _bounds : RecalculateBounds();

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
                GC.SuppressFinalize(this);
                Renderer.Dispose();
                Node.Dispose();
            }
        }
    }

    /// <summary>Creates a renderer for one declared purpose.</summary>
    /// <param name="width">The output width in logical units.</param>
    /// <param name="height">The output height in logical units.</param>
    /// <param name="intent">
    /// What the output is for. This is not a hint: it decides whether an intermediate allocation failure
    /// drops the contribution or fails the render, so it has no safe default - a delivery host that let it
    /// default would export a frame missing whatever could not be allocated.
    /// </param>
    /// <param name="renderScale">The output scale applied to the logical size.</param>
    /// <param name="maxWorkingScale">The global upper bound on any intermediate's working density.</param>
    public Renderer(
        int width,
        int height,
        RenderIntent intent,
        float renderScale = 1f,
        float maxWorkingScale = float.PositiveInfinity)
        : this(
            width,
            height,
            intent,
            renderScale,
            maxWorkingScale,
            surface: null)
    {
    }

    // Test seam: only tests pass a surface or a dispatcher, so they can render on a CPU target.
    internal Renderer(
        int width,
        int height,
        RenderIntent intent,
        float renderScale,
        float maxWorkingScale,
        RenderTarget? surface,
        Dispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher ?? RenderThread.Dispatcher;

        if (!Enum.IsDefined(intent))
        {
            // This constructor owns `surface` from entry.
            surface?.Dispose();
            throw new ArgumentOutOfRangeException(nameof(intent), intent, "Unknown render intent.");
        }

        float outputScale = RenderScaleUtilities.SanitizeOutputScale(renderScale);
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
                    actualSurface = surface ?? CreateRootSurface(DeviceSize);
                    if (actualSurface.Width != DeviceSize.Width || actualSurface.Height != DeviceSize.Height)
                    {
                        throw new ArgumentException(
                            "The injected render target must match the renderer device size.",
                            nameof(surface));
                    }

                    var canvas = new ImmediateCanvas(actualSurface, intent, outputScale, maxScale,
                        logicalSize: FrameSize.ToSize(1));
                    return (canvas, actualSurface);
                }
                catch
                {
                    actualSurface?.Dispose();
                    throw;
                }
            });
        }
        catch
        {
            _frameRenderer.Dispose();
            _completeTarget.Dispose();
            _frameClear.Dispose();

            throw;
        }
    }

    /// <summary>Allocates the root output surface, refusing an extent the device cannot attach.</summary>
    /// <remarks>
    /// Unlike a pooled intermediate, the root goes straight to the allocator, so it is measured here before an
    /// over-scaled export reaches the driver as an attachment it may answer with undefined behaviour.
    /// </remarks>
    private static RenderTarget CreateRootSurface(PixelSize deviceSize)
    {
        BufferDimensionBudget budget = BufferDimensionBudget.Resolve(BufferBudgetScope.Allocation);
        if (!budget.Fits(deviceSize))
            throw RenderTargetPool.CreateAllocationFailure(deviceSize, budget.MaxDimension);

        return RenderTarget.Create(deviceSize.Width, deviceSize.Height)
               ?? throw new InvalidOperationException(
                   $"Could not create a canvas of this size. (width: {deviceSize.Width}, height: {deviceSize.Height})");
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

    public MediaTime Time { get; internal set; }

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

    internal RenderIntent FrameRequestIntent
        => _frameRenderer.DefaultRequest.Intent;

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

        GC.SuppressFinalize(this);
        OnDispose(true);
        GpuResourceRelease.Run(_dispatcher, DisposeRenderResourcesCore);
    }

    private static void VerifyCleanupAccess(Dispatcher dispatcher)
    {
        if (!dispatcher.CheckAccess() && !dispatcher.HasShutdownFinished)
        {
            dispatcher.VerifyAccess();
        }
    }

    private void DisposeRenderResourcesCore()
    {
        VerifyCleanupAccess(_dispatcher);
        _completeTarget?.UpdateRoots([]);
        _frameRenderer?.Dispose();
        _completeTarget?.Dispose();
        _frameClear?.Dispose();
        _immediateCanvas?.Dispose();
        _surface?.Dispose();
        ClearEntryCachesCore();
        DisposeAllEntriesCore();
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
        frame.CancellationToken.ThrowIfCancellationRequested();
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
            _frameRenderer.Render(_immediateCanvas, null, frame.CancellationToken);
        }
        finally
        {
            ClearFrame();
        }

        InvalidateEntryBounds(pendingEntries);
        _allCurrentEntries.AddRange(pendingEntries);
        _frameContainsScene3D = frame.ContainsScene3D;
    }

    private void PrepareEntries(CompositionFrame frame, List<Entry> destination)
    {
        foreach (var obj in frame.Objects)
        {
            frame.CancellationToken.ThrowIfCancellationRequested();
            if (obj is not Drawable.Resource drawableResource)
                continue;

            destination.Add(PrepareDrawable(drawableResource));
        }
    }

    private static void InvalidateEntryBounds(List<Entry> entries)
    {
        foreach (Entry entry in entries)
        {
            entry.InvalidateBounds();
        }
    }

    private Entry PrepareDrawable(Drawable.Resource resource)
    {
        Drawable drawable = resource.RequireOriginal();
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
                resource.Render(ctx);
            }
            catch
            {
                entry.Node.MarkChanged();
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
        RenderCacheOptions? cacheOptions = null,
        Rect? targetDomain = null)
        => new(
            node,
            new RenderNodeRenderRequest
            {
                Intent = Intent,
                TargetDomain = targetDomain ?? new Rect(default, FrameSize.ToSize(1)),
                OutputScale = OutputScale,
                MaxWorkingScale = MaxWorkingScale,
                CacheOptions = cacheOptions ?? CacheOptions,
                Purpose = purpose,
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
            entry.Dispose();
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
        InvalidateEntryBounds(pendingEntries);
        _allCurrentEntries.AddRange(pendingEntries);
        _frameContainsScene3D = frame.ContainsScene3D;
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
                Drawable.Resource? hit = entry.Node.Drawable?.Resource;
                // A presenter's source may cover more than its content. Select an actual drawable,
                // continuing to the layers beneath when only the matte was under the pointer.
                if (HitTestPresented(entry.Node, hit, point) is { } drawable)
                    return drawable;
            }
        }

        return null;
    }

    // Select a presenter's content, including a matte inside a transition. A transition tries the
    // incoming side first, mapping the point through each side's transforms and clips.
    private Drawable? HitTestPresented(DrawableRenderNode node, Drawable.Resource? resource, Point point)
    {
        if (resource is not IContentPresenter presenter)
            return resource?.GetOriginal();

        var children = new Dictionary<Drawable.Resource, (DrawableRenderNode Node, RenderNode[] Path)>(
            ReferenceEqualityComparer.Instance);
        CollectDrawableNodes(node, [], children);
        foreach (IReadOnlyList<Drawable.Resource> side in presenter.PresentedSides)
        {
            for (int i = side.Count - 1; i >= 0; i--)
            {
                if (!children.TryGetValue(side[i], out (DrawableRenderNode Node, RenderNode[] Path) child)
                    || MapIntoDrawable(point, child.Path) is not { } local)
                {
                    continue;
                }

                using RenderNodeRenderer renderer = CreateEntryRenderer(child.Node);
                if (renderer.HitTest(local) && HitTestPresented(child.Node, side[i], local) is { } hit)
                {
                    return hit;
                }
            }
        }

        return presenter.UnhitTarget;
    }

    // Each drawable node with the container nodes above it, outermost first.
    private static void CollectDrawableNodes(
        ContainerRenderNode container,
        List<RenderNode> path,
        Dictionary<Drawable.Resource, (DrawableRenderNode Node, RenderNode[] Path)> nodes)
    {
        foreach (RenderNode child in container.Children)
        {
            if (child is DrawableRenderNode { Drawable.Resource: { } resource } drawable)
            {
                nodes.TryAdd(resource, (drawable, [.. path]));
                if (resource is IContentPresenter)
                {
                    path.Add(drawable);
                    CollectDrawableNodes(drawable, path, nodes);
                    path.RemoveAt(path.Count - 1);
                }
            }
            else if (child is ContainerRenderNode inner)
            {
                path.Add(inner);
                CollectDrawableNodes(inner, path, nodes);
                path.RemoveAt(path.Count - 1);
            }
        }
    }

    // The node an active transition draws drawable with, and the container nodes above it, outermost first.
    // Null when no transition draws it.
    private (DrawableRenderNode Node, RenderNode[] Path)? FindTransitionedNode(Drawable drawable)
    {
        foreach (Entry entry in _allCurrentEntries)
        {
            if (entry.Node.Drawable?.Resource is not IContentPresenter) continue;

            var children = new Dictionary<Drawable.Resource, (DrawableRenderNode Node, RenderNode[] Path)>(
                ReferenceEqualityComparer.Instance);
            CollectDrawableNodes(entry.Node, [], children);
            foreach (KeyValuePair<Drawable.Resource, (DrawableRenderNode Node, RenderNode[] Path)> child in children)
            {
                if (ReferenceEquals(child.Key.GetOriginal(), drawable))
                {
                    return child.Value;
                }
            }
        }

        return null;
    }

    // The node another entry, such as a group's, draws drawable with, and the container nodes above it, outermost
    // first. Null when no entry draws it inside itself.
    private (DrawableRenderNode Node, RenderNode[] Path)? FindNestedNode(Drawable drawable)
    {
        var path = new List<RenderNode>();
        foreach (Entry entry in _allCurrentEntries)
        {
            if (Find(entry.Node) is { } node) return (node, [.. path]);
        }

        return null;

        DrawableRenderNode? Find(ContainerRenderNode container)
        {
            foreach (RenderNode child in container.Children)
            {
                if (child is DrawableRenderNode { Drawable.Resource: { } resource } node
                    && ReferenceEquals(resource.GetOriginal(), drawable))
                {
                    return node;
                }

                if (child is ContainerRenderNode inner)
                {
                    path.Add(inner);
                    if (Find(inner) is { } found) return found;
                    path.RemoveAt(path.Count - 1);
                }
            }

            return null;
        }
    }

    // The bounds of drawable as an active transition draws it: measured on its own, cut to the clips the
    // transition draws it within and mapped out through the transforms. Null when no transition draws it.
    private Rect? GetTransitionedBoundary(Drawable drawable)
    {
        if (FindTransitionedNode(drawable) is not { } child) return null;

        using RenderNodeRenderer renderer = CreateEntryRenderer(child.Node);
        return MapOutOfDrawable(renderer.Measure().QueryBounds, child.Path);
    }

    private static Rect MapOutOfDrawable(Rect bounds, RenderNode[] path)
    {
        for (int i = path.Length - 1; i >= 0; i--)
        {
            switch (path[i])
            {
                case RectClipRenderNode { Operation: ClipOperation.Intersect } clip:
                    bounds = bounds.Intersect(clip.Clip);
                    break;
                case TransformRenderNode { TransformOperator: TransformOperator.Prepend } transform:
                    bounds = bounds.TransformToAABB(transform.Transform);
                    break;
            }
        }

        return bounds;
    }

    // Maps a point into the space a drawable is drawn in through the transforms above it, or returns null
    // when a clip above it hides that point. Masks are not evaluated, so a masked drawable still answers
    // for points its mask hides.
    private static Point? MapIntoDrawable(Point point, RenderNode[] path)
    {
        foreach (RenderNode node in path)
        {
            switch (node)
            {
                case TransformRenderNode { TransformOperator: TransformOperator.Prepend } transform:
                    if (!transform.Transform.TryInvert(out Matrix inverse)) return null;
                    point = inverse.Transform(point);
                    break;
                case RectClipRenderNode clip
                    when clip.Clip.Contains(point) != (clip.Operation == ClipOperation.Intersect):
                    return null;
            }
        }

        return point;
    }

    public Rect[] GetBoundaries(int zIndex) => GetBoundariesCore(zIndex, recalculate: false);

    public Rect? GetBoundary(Drawable drawable)
    {
        _dispatcher.VerifyAccess();
        // A drawable inside an active transition is drawn by the transition's presenter, so it has no entry
        // of its own in this frame.
        if (GetTransitionedBoundary(drawable) is { } transitioned)
        {
            return transitioned;
        }

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

    /// <summary>
    /// Gets the current-frame bounds of a drawable in its own space, inside the transform it is drawn under, with
    /// the matrix that places that space in the frame.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> when the drawable is not in the current frame, draws nothing, does not draw its
    /// content under a transform of its own the way <see cref="Drawable.Resource.RenderCore"/> does, or is drawn under a
    /// perspective transform that takes part of the box past its camera plane.
    /// </returns>
    /// <remarks>
    /// The bounds are what <see cref="GetBoundary"/> measures before the transform maps them, so the box they make
    /// turns with a rotated drawable and its axis-aligned extent is that boundary. Content sized by the frame it
    /// draws into rather than by itself, such as an effect without bounds of its own, has no such box: its boundary
    /// is the frame however the drawable is transformed, so the drawable's layout box is returned for it instead. A
    /// drawable an active transition or a group draws is placed through their transforms too; the transition's clips
    /// are not applied.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The caller does not have render-thread access.</exception>
    internal (Rect Bounds, Matrix Transform)? GetLocalBoundary(Drawable drawable)
    {
        _dispatcher.VerifyAccess();
        (DrawableRenderNode Node, RenderNode[] Path)? found = FindTransitionedNode(drawable);
        if (found is null
            && _nodeCache.TryGetValue(drawable, out Entry? entry)
            && _allCurrentEntries.Contains(entry))
        {
            found = (entry.Node, []);
        }

        found ??= FindNestedNode(drawable);

        // Drawable.Resource.RenderCore pushes the blend mode, then the transform its content is drawn under.
        if (found is not { } drawn
            || drawn.Node.Children is not [BlendModeRenderNode
            {
                Children: [TransformRenderNode { TransformOperator: TransformOperator.Prepend } transform],
            }])
        {
            return null;
        }

        Rect bounds = MeasureContent(null);
        if (!HasArea(bounds)) return null;

        // Content sized by the frame answers any frame with that frame. Measured against a frame moved off its own,
        // it gives other bounds, and its box would snap back to the frame after every scale.
        Rect frame = new(default, FrameSize.ToSize(1));
        if (MeasureContent(frame.Translate(new Vector(frame.Width, frame.Height))) != bounds)
        {
            if (drawn.Node.Drawable?.Resource is not { } resource) return null;
            bounds = new Rect(resource.Measure(frame.Size));
            if (!HasArea(bounds)) return null;
        }

        Matrix outerMatrix = Matrix.Identity;
        foreach (RenderNode node in drawn.Path)
        {
            if (node is TransformRenderNode { TransformOperator: TransformOperator.Prepend } outer)
            {
                outerMatrix = outer.Transform * outerMatrix;
            }
            else if (node is DrawableGroup.CustomTransformRenderNode group)
            {
                outerMatrix = group.Matrix * outerMatrix;
            }
        }

        // A perspective transform maps the corners onto the quad drawn between them only while all of them stay in
        // front of its camera plane. A corner past it is reflected through the origin, where GetBoundary clips.
        Matrix matrix = transform.Transform * outerMatrix;
        if (matrix.ContainsPerspective())
        {
            foreach (Point corner in (ReadOnlySpan<Point>)[bounds.TopLeft, bounds.TopRight, bounds.BottomRight, bounds.BottomLeft])
            {
                if (!(matrix.GetTransformDivisor(corner) >= Rect.DefaultNearPlane)) return null;
            }
        }

        return (bounds, matrix);

        Rect MeasureContent(Rect? targetDomain)
        {
            Rect measured = default;
            foreach (RenderNode content in transform.Children)
            {
                using RenderNodeRenderer renderer = CreateEntryRenderer(content, targetDomain: targetDomain);
                measured = measured.Union(renderer.Measure().QueryBounds);
            }

            return measured;
        }

        static bool HasArea(Rect rect)
            => RenderRectValidation.IsFiniteNonNegative(rect) && rect.Width > 0 && rect.Height > 0;
    }

    /// <summary>Recalculates and caches current-frame bounds for drawables at the specified z-index.</summary>
    /// <remarks>This method must be called on the render thread.</remarks>
    /// <exception cref="InvalidOperationException">The caller does not have render-thread access.</exception>
    public Rect[] RecalculateBoundaries(int zIndex) => GetBoundariesCore(zIndex, recalculate: true);

    private Rect[] GetBoundariesCore(int zIndex, bool recalculate)
    {
        _dispatcher.VerifyAccess();
        return [.. _allCurrentEntries
            .Where(e => e.Node.Drawable?.Resource.RequireOriginal().ZIndex.Value == zIndex)
            .Select(e => recalculate ? e.RecalculateBounds() : e.GetBounds())];
    }

    public DrawableRenderNode? FindRenderNode(Drawable drawable)
    {
        _dispatcher.VerifyAccess();
        if (_nodeCache.TryGetValue(drawable, out Entry? entry))
        {
            return entry.Node;
        }

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
            // A drawable node is a container too, so it is compared before the nodes it holds are searched.
            if (child is DrawableRenderNode childDrawable &&
                childDrawable.Drawable?.Resource.GetOriginal() == drawable)
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
        _dispatcher.VerifyAccess();
        return _surface.Snapshot();
    }

    // A cached 3D preview may contain a handle even after its selection changed. Replay 3D frames
    // without gizmos, preserving both the editor surface and preview allocation-failure behavior.
    internal Bitmap? SnapshotWithoutGizmos()
    {
        _dispatcher.VerifyAccess();
        if (!_frameContainsScene3D && !_allCurrentEntries.Any(entry => ContainsScene3DNode(entry.Node)))
            return null;

        _completeTarget.UpdateRoots(_allCurrentEntries.Select(static entry => (RenderNode)entry.Node));
        using var raster = _frameRenderer.Rasterize(_frameRenderer.DefaultRequest with
        {
            SuppressGizmos = true,
            // Editor output caches may contain handles; do not read or replace them for this replay.
            CacheOptions = RenderCacheOptions.Disabled,
            ManageCacheLifecycle = false,
            Purpose = RenderRequestPurpose.Auxiliary,
            RequestedRegion = new Rect(default, FrameSize.ToSize(1)),
        });
        return raster.Bitmap!.Clone();

        static bool ContainsScene3DNode(RenderNode node)
        {
            // Also support render nodes supplied directly, outside composition reconciliation.
            if (node is Graphics3D.Scene3DRenderNode)
                return true;

            foreach (RenderNode child in node.ChildNodes)
                if (ContainsScene3DNode(child))
                    return true;
            return false;
        }
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
    /// Reads the current surface back without waiting for the GPU. See <see cref="RenderTarget.SnapshotAsync()"/>.
    /// </summary>
    public Task<Bitmap> SnapshotAsync()
    {
        _dispatcher.VerifyAccess();
        return _surface.SnapshotAsync();
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
        _completeTarget.UpdateRoots([]);
        RenderNodeRenderer replacement = CreateEntryRenderer(
            _completeTarget,
            RenderRequestPurpose.Frame,
            cacheOptions);
        RenderNodeRenderer previous = _frameRenderer;
        _frameRenderer = replacement;
        if (updateCacheOptions)
            _cacheOptions = cacheOptions;
        previous.Dispose();
        ClearEntryCachesCore();
    }

    private void ClearEntryCachesCore()
    {
        VerifyCleanupAccess(_dispatcher);
        var entries = _nodeCache.ToArray();
        _nodeCache.Clear();
        _allCurrentEntries.Clear();
        DisposeEntries(entries);
    }

    private void DisposeAllEntriesCore()
    {
        VerifyCleanupAccess(_dispatcher);
        var entries = _nodeCache.ToArray();
        _nodeCache.Clear();
        DisposeEntries(entries);
    }

    private static void DisposeEntries(KeyValuePair<Drawable, Entry>[] entries)
    {
        foreach (var item in entries)
        {
            item.Value.Dispose();
        }
    }

    private void DispatchFinalizerRenderResourceCleanup()
    {
        GpuResourceRelease.DispatchFinalizer(_dispatcher, () =>
        {
            try
            {
                DisposeRenderResourcesCore();
            }
            catch (Exception ex)
            {
                s_logger.LogDebug(
                    ex,
                    "Renderer finalizer: render resource cleanup threw during last-resort disposal");
            }
        });
    }

    public static ImmediateCanvas GetInternalCanvas(Renderer renderer)
    {
        return renderer._immediateCanvas;
    }
}
