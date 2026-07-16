using Beutl.Collections.Pooled;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public class RenderNodeProcessor
{
    private readonly bool _useRenderCache;
    private readonly Func<int, int, RenderTarget?>? _inheritedRenderTargetFactory;

    public RenderNodeProcessor(
        RenderNode root,
        bool useRenderCache,
        RenderIntent renderIntent,
        float outputScale = 1f,
        float maxWorkingScale = float.PositiveInfinity,
        PipelineDiagnostics? diagnostics = null,
        RenderPullPurpose pullPurpose = RenderPullPurpose.Frame)
        : this(
            pool: null, root, useRenderCache, renderIntent, outputScale, maxWorkingScale, diagnostics,
            pullPurpose)
    {
    }

    internal RenderNodeProcessor(
        RenderTargetPool? pool,
        RenderNode root,
        bool useRenderCache,
        RenderIntent renderIntent,
        float outputScale = 1f,
        float maxWorkingScale = float.PositiveInfinity,
        PipelineDiagnostics? diagnostics = null,
        RenderPullPurpose pullPurpose = RenderPullPurpose.Frame,
        Func<int, int, RenderTarget?>? renderTargetFactory = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        Root = root;
        _useRenderCache = useRenderCache;
        OutputScale = float.IsFinite(outputScale) && outputScale > 0f ? outputScale : 1f;
        MaxWorkingScale = RenderNodeContext.SanitizeMaxWorkingScale(maxWorkingScale);
        Diagnostics = diagnostics ?? new PipelineDiagnostics();
        Pool = pool;
        RenderIntent = RenderPolicyValidation.Validate(renderIntent, nameof(renderIntent));
        PullPurpose = RenderPolicyValidation.Validate(pullPurpose, nameof(pullPurpose));
        _inheritedRenderTargetFactory = renderTargetFactory;
    }

    public RenderNode Root { get; }

    /// <summary>Output scale <c>s_out</c> seeded into every <see cref="RenderNodeContext"/>. Sanitized to positive-finite.</summary>
    public float OutputScale { get; }

    /// <summary>Working-scale ceiling seeded into every <see cref="RenderNodeContext"/>. <c>+Inf</c> = no ceiling.</summary>
    public float MaxWorkingScale { get; }

    /// <summary>
    /// Effect-pipeline counters seeded into every pulled <see cref="RenderNodeContext"/>. A renderer that
    /// creates processors per frame hands in its own instance so counts accumulate per renderer; a
    /// standalone processor owns a fresh one.
    /// </summary>
    public PipelineDiagnostics Diagnostics { get; }

    /// <summary>
    /// Render-target pool seeded into every pulled <see cref="RenderNodeContext"/>, or <see langword="null"/>
    /// to allocate effect intermediates directly (behavior-identical to the pre-pool pipeline).
    /// </summary>
    internal RenderTargetPool? Pool { get; }

    /// <summary>Preview/delivery failure policy seeded into every pulled node context.</summary>
    public RenderIntent RenderIntent { get; }

    /// <summary>The logical output region requested by this pull's parent; invalid means full output.</summary>
    public Rect RequestedBounds { get; init; } = Rect.Invalid;

    /// <summary>
    /// Overrides input-subtree stability for every context in this nested pull when the parent supplies opaque input.
    /// </summary>
    internal bool? InputSubtreeStableOverride { get; init; }

    /// <summary>Marks this pull as hit-test/bounds-only work that must preserve frame-render cache state.</summary>
    public RenderPullPurpose PullPurpose { get; }

    /// <summary>
    /// Allocates the intermediate <see cref="RenderTarget"/> used to rasterize each operation.
    /// Override to substitute a custom allocation (e.g. pooling). Defaults to <see cref="RenderTarget.Create"/>.
    /// </summary>
    protected virtual RenderTarget? CreateRenderTarget(int width, int height)
        => _inheritedRenderTargetFactory is { } factory
            ? factory(width, height)
            : RenderTarget.Create(width, height);

    public void Render(ImmediateCanvas canvas)
    {
        var ops = PullToRoot();
        int consumed = 0;
        try
        {
            foreach (var op in ops)
            {
                op.Render(canvas);
                consumed++;
                op.Dispose();
            }
        }
        catch
        {
            RenderNodeOperation.DisposeAll(ops.AsSpan(consumed));
            throw;
        }
    }

    /// <summary>
    /// Rasterizes one operation into its own render target at scale <paramref name="w"/>.
    /// Returns <see langword="null"/> for zero-area. The op is always disposed.
    /// </summary>
    internal (RenderTarget RenderTarget, Rect Bounds)? RasterizeAt(RenderNodeOperation op, float w)
    {
        RenderTarget? renderTarget = null;
        bool opDisposeStarted = false;
        try
        {
            Rect opBounds = op.Bounds;
            var rect = w == 1f ? PixelRect.FromRect(opBounds) : PixelRect.FromRect(opBounds, w);
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                opDisposeStarted = true;
                op.Dispose();
                return null;
            }

            renderTarget = CreateRenderTarget(rect.Width, rect.Height);
            if (renderTarget == null)
            {
                // Defer op disposal to the catch's best-effort path so a throwing op.Dispose()
                // cannot mask the null-allocation failure.
                throw new Exception("RenderTarget is null");
            }

            using var canvas = new ImmediateCanvas(
                renderTarget, RenderIntent, w, MaxWorkingScale, logicalSize: opBounds.Size,
                pullPurpose: PullPurpose);
            canvas.Clear();

            using (canvas.PushTransform(Matrix.CreateTranslation(-opBounds.X, -opBounds.Y)))
            {
                op.Render(canvas);
                opDisposeStarted = true;
                op.Dispose();
            }

            return (renderTarget, opBounds);
        }
        catch
        {
            // renderTarget.Dispose() is GPU-native teardown that can itself throw; swallow cleanup
            // faults so the in-flight render exception propagates and the op is still disposed.
            DisposeBestEffort(renderTarget);
            if (!opDisposeStarted)
                DisposeBestEffort(op);
            throw;
        }
    }

    internal List<(RenderTarget RenderTarget, Rect Bounds)> RasterizeToRenderTargets()
    {
        return RasterizeToRenderTargets(PullToRoot());
    }

    /// <summary>Rasterizes already-pulled operations at <see cref="OutputScale"/>. Each op is consumed by <see cref="RasterizeAt"/>.</summary>
    internal List<(RenderTarget RenderTarget, Rect Bounds)> RasterizeToRenderTargets(RenderNodeOperation[] ops)
    {
        var list = new List<(RenderTarget, Rect)>();
        int consumed = 0;
        try
        {
            foreach (var op in ops)
            {
                consumed++;
                if (RasterizeAt(op, OutputScale) is { } result)
                {
                    list.Add(result);
                }
            }

            return list;
        }
        catch
        {
            // Clean up remaining ops (RasterizeAt already disposed the faulting one).
            RenderNodeOperation.DisposeAll(ops.AsSpan(consumed));
            DisposeRenderTargets(list);
            throw;
        }
    }

    public List<Bitmap> Rasterize()
    {
        var list = new List<Bitmap>();
        var ops = PullToRoot();
        int consumed = 0;
        try
        {
            foreach (var op in ops)
            {
                consumed++;
                if (RasterizeAt(op, OutputScale) is { } result)
                {
                    try
                    {
                        list.Add(result.RenderTarget.Snapshot());
                    }
                    finally
                    {
                        // Best-effort: a throwing GPU-native teardown must not discard the bitmap
                        // just snapshotted from this target.
                        DisposeBestEffort(result.RenderTarget);
                    }
                }
            }

            return list;
        }
        catch
        {
            RenderNodeOperation.DisposeAll(ops.AsSpan(consumed));
            DisposeBitmaps(list);
            throw;
        }
    }

    public Bitmap RasterizeAndConcat()
    {
        var ops = PullToRoot();
        RenderTarget? renderTarget = null;
        ImmediateCanvas? canvas = null;
        int consumed = 0;
        try
        {
            // Bounds are owned-operation state too: a custom operation may throw while exposing
            // them, so aggregation belongs inside the same cleanup boundary as rendering.
            var bounds = ops.Aggregate(Rect.Empty, (a, n) => a.Union(n.Bounds));
            float w = OutputScale;
            var rect = w == 1f ? PixelRect.FromRect(bounds) : PixelRect.FromRect(bounds, w);
            renderTarget =
                CreateRenderTarget(rect.Width, rect.Height) ?? throw new Exception("RenderTarget is null");
            canvas = new ImmediateCanvas(
                renderTarget, RenderIntent, w, MaxWorkingScale, logicalSize: bounds.Size,
                pullPurpose: PullPurpose);
            canvas.Clear();

            using (canvas.PushTransform(Matrix.CreateTranslation(-bounds.X, -bounds.Y)))
            {
                foreach (var op in ops)
                {
                    op.Render(canvas);
                    consumed++;
                    op.Dispose();
                }
            }

            return renderTarget.Snapshot();
        }
        catch
        {
            RenderNodeOperation.DisposeAll(ops.AsSpan(consumed));
            throw;
        }
        finally
        {
            // Best-effort on both success and failure: a throwing GPU-native teardown must neither
            // mask an in-flight render exception nor discard a successfully snapshotted bitmap.
            DisposeBestEffort(canvas);
            DisposeBestEffort(renderTarget);
        }
    }

    private static void DisposeRenderTargets(List<(RenderTarget RenderTarget, Rect Bounds)> targets)
    {
        foreach (var item in targets)
        {
            DisposeBestEffort(item.RenderTarget);
        }
    }

    private static void DisposeBitmaps(List<Bitmap> bitmaps)
    {
        foreach (var bmp in bitmaps)
        {
            DisposeBestEffort(bmp);
        }
    }

    private static void DisposeBestEffort(IDisposable? disposable)
    {
        if (disposable == null)
            return;

        try
        {
            disposable.Dispose();
        }
        catch
        {
            // Preserve the original render/rasterize failure while still sweeping the rest.
        }
    }

    public RenderNodeOperation[] PullToRoot()
    {
        return Pull(Root);
    }

    public RenderNodeOperation[] Pull(RenderNode node)
    {
        return Pull(node, RequestedBounds);
    }

    private RenderNodeOperation[] Pull(RenderNode node, Rect requestedBounds)
    {
        bool usePersistentCache = _useRenderCache && PullPurpose == RenderPullPurpose.Frame;
        if (usePersistentCache && node.Cache is { } cache
            && cache.IsCachedFor(RenderIntent, PullPurpose))
        {
            // Replay tiles with the density they were rasterized at.
            return cache.UseCache()
                .Select(i => RenderNodeOperation.CreateFromRenderTarget(
                    bounds: i.Bounds,
                    position: i.Bounds.Position,
                    renderTarget: i.RenderTarget,
                    effectiveScale: EffectiveScale.At(cache.Density)))
                .ToArray();
        }

        RenderNodeOperation[] input = [];
        if (node is ContainerRenderNode container)
        {
            Rect childRequestedBounds = node switch
            {
                // A filter effect's backward ROI is not known until after its children have been pulled and its
                // graph has been described. Forwarding the outer request here would bake a nested effect to that
                // narrow rect before an expanding parent effect can request its halo.
                FilterEffectRenderNode => Rect.Invalid,
                TransformRenderNode transform => transform.MapRequestedBoundsToChild(requestedBounds),
                _ => requestedBounds,
            };
            using var operations = new PooledList<RenderNodeOperation>();
            try
            {
                foreach (RenderNode innerNode in container.Children)
                {
                    operations.AddRange(Pull(innerNode, childRequestedBounds));
                }

                input = operations.ToArray();
            }
            catch
            {
                // A later child pull can fail after earlier siblings produced operations. Preserve that
                // pull failure while sweeping every already-produced operation, including faulting disposals.
                RenderNodeOperation.DisposeAll(operations.Span);
                throw;
            }
        }

        var context = new RenderNodeContext(input, RenderIntent, OutputScale, MaxWorkingScale, PullPurpose)
        {
            // Persistent caches are frame-only. Auxiliary pulls always execute without consulting or mutating retained
            // frame state; a frame node may still CLEAR this flag to opt its subtree out (read back below).
            IsRenderCacheEnabled = usePersistentCache,
            Diagnostics = Diagnostics,
            Pool = Pool,
            RenderTargetFactory = _inheritedRenderTargetFactory ?? CreateRenderTarget,
            InputSubtreeStableOverride = InputSubtreeStableOverride,
            RequestedBounds = requestedBounds,
        };
        RenderNodeOperation[]? result = null;
        try
        {
            result = node.Process(context);
            if (usePersistentCache && !context.IsRenderCacheEnabled)
            {
                node.Cache.ReportRenderCount(0);
            }

            return result;
        }
        catch
        {
            // Process may transfer input ownership into wrappers in its result. Dispose result-only
            // operations first, then sweep every input. RenderNodeOperation is state-first/idempotent,
            // so wrappers that release an input cannot make the second sweep unsafe.
            if (result != null)
            {
                foreach (RenderNodeOperation operation in result)
                {
                    bool isInput = false;
                    foreach (RenderNodeOperation inputOperation in input)
                    {
                        if (ReferenceEquals(operation, inputOperation))
                        {
                            isInput = true;
                            break;
                        }
                    }

                    if (!isInput)
                    {
                        try
                        {
                            operation.Dispose();
                        }
                        catch
                        {
                            // Preserve the Process/bookkeeping failure while continuing the sweep.
                        }
                    }
                }
            }

            RenderNodeOperation.DisposeAll(input);
            throw;
        }
    }
}
