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

    /// <summary>
    /// The output scale <c>s_out</c> seeded into every <see cref="RenderNodeContext"/> this processor
    /// pulls (feature 003). <c>1.0</c> = logical == device (byte-identical to pre-feature). Sanitized to a
    /// positive-finite value so a degenerate scale can never corrupt the <see cref="RasterizeAt"/> sizing
    /// (<c>PixelRect.FromRect(bounds, w)</c>) or flow downstream — mirrors the same guard in
    /// <see cref="Renderer"/> and <see cref="RenderNodeContext"/>.
    /// </summary>
    public float OutputScale { get; } = float.IsFinite(outputScale) && outputScale > 0f ? outputScale : 1f;

    /// <summary>
    /// The working-scale ceiling (FR-037) seeded into every <see cref="RenderNodeContext"/> this processor
    /// pulls. <c>+∞</c> (default) = no ceiling.
    /// </summary>
    public float MaxWorkingScale { get; } = maxWorkingScale;

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
    /// Rasterizes a single operation into its own render target at working scale <paramref name="w"/>.
    /// The <c>w == 1</c> path is the exact pre-feature path (byte-identical); for <c>w != 1</c> the
    /// target is sized <c>ceil(bounds × w)</c> and a <see cref="Matrix.CreateScale"/> is pushed.
    /// Returns <see langword="null"/> for an empty (zero-area) target. The op is disposed in all cases
    /// (whether it was rendered or skipped as empty), so the caller never owns it afterward.
    /// </summary>
    internal (RenderTarget RenderTarget, Rect Bounds)? RasterizeAt(RenderNodeOperation op, float w)
    {
        var rect = w == 1f ? PixelRect.FromRect(op.Bounds) : PixelRect.FromRect(op.Bounds, w);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            // A zero-area op still owns a backing surface; dispose it instead of leaking on the empty path.
            op.Dispose();
            return null;
        }

        var renderTarget = RenderTarget.Create(rect.Width, rect.Height);
        if (renderTarget == null)
        {
            // Dispose the op before the fatal throw so the doc's "disposed in all cases" holds literally.
            op.Dispose();
            throw new Exception("RenderTarget is null");
        }

        // feature 003: the canvas bakes the working-density base CTM CreateScale(w) (identity at w == 1) and
        // tags its surface density w for any backdrop captured here. The op only needs the logical translation
        // to its bounds origin; w == 1 stays byte-identical (no base Save, translation-only).
        using var canvas = new ImmediateCanvas(renderTarget, w, MaxWorkingScale, logicalSize: op.Bounds.Size);
        canvas.Clear();

        using (canvas.PushTransform(Matrix.CreateTranslation(-op.Bounds.X, -op.Bounds.Y)))
        {
            op.Render(canvas);
            op.Dispose();
        }

        return (renderTarget, op.Bounds);
    }

    internal List<(RenderTarget RenderTarget, Rect Bounds)> RasterizeToRenderTargets()
    {
        return RasterizeToRenderTargets(PullToRoot());
    }

    /// <summary>
    /// Rasterizes already-pulled operations at <see cref="OutputScale"/>. Split from the param-less overload so
    /// a caller (the render cache) can inspect the pulled ops' <see cref="RenderNodeOperation.EffectiveScale"/>
    /// before committing to rasterize them — each op is consumed (disposed) by <see cref="RasterizeAt"/>.
    /// </summary>
    internal List<(RenderTarget RenderTarget, Rect Bounds)> RasterizeToRenderTargets(RenderNodeOperation[] ops)
    {
        var list = new List<(RenderTarget, Rect)>();
        foreach (var op in ops)
        {
            if (RasterizeAt(op, OutputScale) is { } result)
            {
                list.Add(result);
            }
        }

        return list;
    }

    public List<Bitmap> Rasterize()
    {
        var list = new List<Bitmap>();
        var ops = PullToRoot();
        foreach (var op in ops)
        {
            // Route through RasterizeAt so this shares its ceil(bounds × w) sizing, base-CTM bake, disposal and
            // zero-area skip instead of re-inlining them (the inlined copy omitted the zero-area guard).
            if (RasterizeAt(op, OutputScale) is { } result)
            {
                using RenderTarget renderTarget = result.RenderTarget;
                list.Add(renderTarget.Snapshot());
            }
        }

        return list;
    }

    public Bitmap RasterizeAndConcat()
    {
        var ops = PullToRoot();
        var bounds = ops.Aggregate(Rect.Empty, (a, n) => a.Union(n.Bounds));
        float w = OutputScale;
        var rect = w == 1f ? PixelRect.FromRect(bounds) : PixelRect.FromRect(bounds, w);
        using var renderTarget =
            RenderTarget.Create(rect.Width, rect.Height) ?? throw new Exception("RenderTarget is null");
        // feature 003: the canvas bakes the working-density base CTM CreateScale(w) (identity at w == 1) and
        // tags its surface density w; the ops only need the logical translation. w == 1 byte-identical.
        using var canvas = new ImmediateCanvas(renderTarget, w, MaxWorkingScale, logicalSize: bounds.Size);
        canvas.Clear();

        using (canvas.PushTransform(Matrix.CreateTranslation(-bounds.X, -bounds.Y)))
        {
            foreach (var op in ops)
            {
                op.Render(canvas);
                op.Dispose();
            }
        }

        return renderTarget.Snapshot();
    }

    public RenderNodeOperation[] PullToRoot()
    {
        return Pull(Root);
    }

    public RenderNodeOperation[] Pull(RenderNode node)
    {
        if (useRenderCache && node.Cache is { IsCached: true } cache)
        {
            // feature 003 (FR-020): replay tiles with the density they were rasterized at — an untagged
            // (Unbounded) replay would erase the subtree's supply-density signal, flipping downstream
            // working scales whenever the cache kicks in.
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
