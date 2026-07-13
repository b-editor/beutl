using Beutl.Collections.Pooled;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public class RenderNodeProcessor(
    RenderNode root,
    bool useRenderCache,
    float outputScale = 1f,
    float maxWorkingScale = float.PositiveInfinity,
    PipelineDiagnostics? diagnostics = null,
    RenderTargetPool? pool = null)
{
    public RenderNode Root { get; } = root;

    /// <summary>Output scale <c>s_out</c> seeded into every <see cref="RenderNodeContext"/>. Sanitized to positive-finite.</summary>
    public float OutputScale { get; } = float.IsFinite(outputScale) && outputScale > 0f ? outputScale : 1f;

    /// <summary>Working-scale ceiling seeded into every <see cref="RenderNodeContext"/>. <c>+Inf</c> = no ceiling.</summary>
    public float MaxWorkingScale { get; } = RenderNodeContext.SanitizeMaxWorkingScale(maxWorkingScale);

    /// <summary>
    /// Effect-pipeline counters seeded into every pulled <see cref="RenderNodeContext"/>. A renderer that
    /// creates processors per frame hands in its own instance so counts accumulate per renderer; a
    /// standalone processor owns a fresh one.
    /// </summary>
    public PipelineDiagnostics Diagnostics { get; } = diagnostics ?? new PipelineDiagnostics();

    /// <summary>
    /// Render-target pool seeded into every pulled <see cref="RenderNodeContext"/>, or <see langword="null"/>
    /// to allocate effect intermediates directly (behavior-identical to the pre-pool pipeline).
    /// </summary>
    public RenderTargetPool? Pool { get; } = pool;

    /// <summary>The logical output region requested by this pull's parent; invalid means full output.</summary>
    public Rect RequestedBounds { get; init; } = Rect.Invalid;

    /// <summary>
    /// Allocates the intermediate <see cref="RenderTarget"/> used to rasterize each operation.
    /// Override to substitute a custom allocation (e.g. pooling). Defaults to <see cref="RenderTarget.Create"/>.
    /// </summary>
    protected virtual RenderTarget? CreateRenderTarget(int width, int height)
        => RenderTarget.Create(width, height);

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
        var rect = w == 1f ? PixelRect.FromRect(op.Bounds) : PixelRect.FromRect(op.Bounds, w);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            op.Dispose();
            return null;
        }

        // A throwing OnDispose leaves the op's IsDisposed false, so the catch keys off this flag
        // (not IsDisposed) to avoid re-disposing — and re-running OnDispose on — an op already torn down.
        RenderTarget? renderTarget = null;
        bool opDisposeStarted = false;
        try
        {
            renderTarget = CreateRenderTarget(rect.Width, rect.Height);
            if (renderTarget == null)
            {
                // Defer op disposal to the catch's best-effort path so a throwing op.Dispose()
                // cannot mask the null-allocation failure.
                throw new Exception("RenderTarget is null");
            }

            using var canvas = new ImmediateCanvas(renderTarget, w, MaxWorkingScale, logicalSize: op.Bounds.Size);
            canvas.Clear();

            Rect opBounds = op.Bounds;
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
        var bounds = ops.Aggregate(Rect.Empty, (a, n) => a.Union(n.Bounds));
        float w = OutputScale;
        var rect = w == 1f ? PixelRect.FromRect(bounds) : PixelRect.FromRect(bounds, w);
        RenderTarget? renderTarget = null;
        ImmediateCanvas? canvas = null;
        int consumed = 0;
        try
        {
            renderTarget =
                CreateRenderTarget(rect.Width, rect.Height) ?? throw new Exception("RenderTarget is null");
            canvas = new ImmediateCanvas(renderTarget, w, MaxWorkingScale, logicalSize: bounds.Size);
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
        if (useRenderCache && node.Cache is { IsCached: true } cache)
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
            using var operations = new PooledList<RenderNodeOperation>();
            foreach (RenderNode innerNode in container.Children)
            {
                operations.AddRange(Pull(innerNode));
            }

            input = operations.ToArray();
        }

        var context = new RenderNodeContext(input, OutputScale, MaxWorkingScale)
        {
            // Seeded from the processor's useRenderCache so cache-consuming nodes (the pass-prefix cache) can honor
            // a caller's disabled render caching; a node may still CLEAR it to opt its subtree out (read back below).
            IsRenderCacheEnabled = useRenderCache,
            Diagnostics = Diagnostics,
            Pool = Pool,
            RequestedBounds = RequestedBounds,
        };
        var result = node.Process(context);
        if (useRenderCache && !context.IsRenderCacheEnabled)
        {
            node.Cache.ReportRenderCount(0);
        }

        return result;
    }
}
