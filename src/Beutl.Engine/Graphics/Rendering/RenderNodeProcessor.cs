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
    /// pulls (feature 003). <c>1.0</c> = logical == device (byte-identical to pre-feature).
    /// </summary>
    public float OutputScale { get; } = outputScale;

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

        var renderTarget = RenderTarget.Create(rect.Width, rect.Height) ??
                           throw new Exception("RenderTarget is null");

        // feature 003 (CSM3-1): rasterized at density w, so tag OutputScale = w for any backdrop captured here.
        using var canvas = new ImmediateCanvas(renderTarget, w);
        canvas.Clear();

        var transform = w == 1f
            ? Matrix.CreateTranslation(-op.Bounds.X, -op.Bounds.Y)
            : Matrix.CreateTranslation(-op.Bounds.X, -op.Bounds.Y) * Matrix.CreateScale(w, w);

        using (canvas.PushTransform(transform))
        {
            op.Render(canvas);
            op.Dispose();
        }

        return (renderTarget, op.Bounds);
    }

    internal List<(RenderTarget RenderTarget, Rect Bounds)> RasterizeToRenderTargets()
    {
        var list = new List<(RenderTarget, Rect)>();
        var ops = PullToRoot();
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
        float w = OutputScale;
        foreach (var op in ops)
        {
            var rect = w == 1f ? PixelRect.FromRect(op.Bounds) : PixelRect.FromRect(op.Bounds, w);
            using var renderTarget = RenderTarget.Create(rect.Width, rect.Height)
                                     ?? throw new Exception("RenderTarget is null");

            // feature 003 (CSM3-1): rasterized at density w, so tag OutputScale = w for any backdrop captured here.
            using var canvas = new ImmediateCanvas(renderTarget, w);
            canvas.Clear();

            var transform = w == 1f
                ? Matrix.CreateTranslation(-op.Bounds.X, -op.Bounds.Y)
                : Matrix.CreateTranslation(-op.Bounds.X, -op.Bounds.Y) * Matrix.CreateScale(w, w);

            using (canvas.PushTransform(transform))
            {
                op.Render(canvas);
                op.Dispose();
            }

            list.Add(renderTarget.Snapshot());
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
        // feature 003 (CSM3-1): rasterized at density w, so tag OutputScale = w for any backdrop captured here.
        using var canvas = new ImmediateCanvas(renderTarget, w);
        canvas.Clear();

        var transform = w == 1f
            ? Matrix.CreateTranslation(-bounds.X, -bounds.Y)
            : Matrix.CreateTranslation(-bounds.X, -bounds.Y) * Matrix.CreateScale(w, w);

        using (canvas.PushTransform(transform))
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
            return cache.UseCache()
                .Select(i => RenderNodeOperation.CreateFromRenderTarget(
                    bounds: i.Bounds,
                    position: i.Bounds.Position,
                    renderTarget: i.RenderTarget))
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
