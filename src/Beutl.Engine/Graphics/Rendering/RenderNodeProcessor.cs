using Beutl.Collections.Pooled;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public class RenderNodeProcessor(
    RenderNode root,
    bool useRenderCache,
    float outputScale = 1f,
    float maxWorkingScale = float.PositiveInfinity)
{
    public RenderNode Root { get; } = root;

    /// <summary>Output scale <c>s_out</c> seeded into every <see cref="RenderNodeContext"/>. Sanitized to positive-finite.</summary>
    public float OutputScale { get; } = float.IsFinite(outputScale) && outputScale > 0f ? outputScale : 1f;

    /// <summary>Working-scale ceiling seeded into every <see cref="RenderNodeContext"/>. <c>+Inf</c> = no ceiling.</summary>
    public float MaxWorkingScale { get; } = RenderNodeContext.SanitizeMaxWorkingScale(maxWorkingScale);

    public void Render(ImmediateCanvas canvas)
    {
        var ops = PullToRoot();
        foreach (var op in ops)
        {
            op.Render(canvas);
            op.Dispose();
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

        var renderTarget = RenderTarget.Create(rect.Width, rect.Height);
        if (renderTarget == null)
        {
            op.Dispose();
            throw new Exception("RenderTarget is null");
        }

        // Set before op.Dispose() so a throwing OnDispose (which leaves IsDisposed false) is not
        // re-disposed by the catch — mirrors the consumed++ guard in Rasterize/RasterizeAndConcat.
        bool opDisposeStarted = false;
        try
        {
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
            DisposeRemainingOperations(ops, consumed);
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
                    using RenderTarget renderTarget = result.RenderTarget;
                    list.Add(renderTarget.Snapshot());
                }
            }

            return list;
        }
        catch
        {
            DisposeRemainingOperations(ops, consumed);
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
        using var renderTarget =
            RenderTarget.Create(rect.Width, rect.Height) ?? throw new Exception("RenderTarget is null");
        using var canvas = new ImmediateCanvas(renderTarget, w, MaxWorkingScale, logicalSize: bounds.Size);
        canvas.Clear();

        int consumed = 0;
        try
        {
            using (canvas.PushTransform(Matrix.CreateTranslation(-bounds.X, -bounds.Y)))
            {
                foreach (var op in ops)
                {
                    op.Render(canvas);
                    consumed++;
                    op.Dispose();
                }
            }
        }
        catch
        {
            DisposeRemainingOperations(ops, consumed);
            throw;
        }

        return renderTarget.Snapshot();
    }

    private static void DisposeRemainingOperations(RenderNodeOperation[] ops, int start)
    {
        for (int j = start; j < ops.Length; j++)
        {
            DisposeBestEffort(ops[j]);
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

    private static void DisposeBestEffort(IDisposable disposable)
    {
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

        var context = new RenderNodeContext(input, OutputScale, MaxWorkingScale);
        var result = node.Process(context);
        if (useRenderCache && !context.IsRenderCacheEnabled)
        {
            node.Cache.ReportRenderCount(0);
        }

        return result;
    }
}
