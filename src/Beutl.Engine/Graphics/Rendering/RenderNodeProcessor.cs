using Beutl.Collections.Pooled;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public class RenderNodeProcessor(RenderNode root, bool useRenderCache)
{
    public RenderNode Root { get; } = root;

    public void Render(ImmediateCanvas canvas)
    {
        var ops = PullToRoot();
        foreach (var op in ops)
        {
            RenderOperation(canvas, op);
            op.Dispose();
        }
    }

    internal List<(RenderTarget RenderTarget, Rect Bounds)> RasterizeToRenderTargets()
    {
        var list = new List<(RenderTarget, Rect)>();
        var ops = PullToRoot();
        foreach (var op in ops)
        {
            var rect = PixelRect.FromRect(op.Bounds);
            if (rect.Width <= 0 || rect.Height <= 0) continue;
            var renderTarget = RenderTarget.Create(rect.Width, rect.Height) ??
                               throw new Exception("RenderTarget is null");

            using var canvas = new ImmediateCanvas(renderTarget);
            canvas.Clear();

            using (canvas.PushTransform(Matrix.CreateTranslation(-op.Bounds.X, -op.Bounds.Y)))
            {
                RenderOperation(canvas, op);
                op.Dispose();
            }

            list.Add((renderTarget, op.Bounds));
        }

        return list;
    }

    public List<Bitmap> Rasterize()
    {
        var list = new List<Bitmap>();
        var ops = PullToRoot();
        foreach (var op in ops)
        {
            var rect = PixelRect.FromRect(op.Bounds);
            using var renderTarget = RenderTarget.Create(rect.Width, rect.Height)
                                     ?? throw new Exception("RenderTarget is null");

            using var canvas = new ImmediateCanvas(renderTarget);
            canvas.Clear();

            using (canvas.PushTransform(Matrix.CreateTranslation(-op.Bounds.X, -op.Bounds.Y)))
            {
                RenderOperation(canvas, op);
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
        var rect = PixelRect.FromRect(bounds);
        using var renderTarget =
            RenderTarget.Create(rect.Width, rect.Height) ?? throw new Exception("RenderTarget is null");
        using var canvas = new ImmediateCanvas(renderTarget);
        canvas.Clear();
        using (canvas.PushTransform(Matrix.CreateTranslation(-bounds.X, -bounds.Y)))
        {
            foreach (var op in ops)
            {
                RenderOperation(canvas, op);
                op.Dispose();
            }
        }

        return renderTarget.Snapshot();
    }

    // Compositor blit per contracts/compositor-blit.md: when an operation declares a non-Identity
    // CorrectionScale, the raster it produced is at op.Bounds.Size / CorrectionScale; push a
    // matrix that scales by CorrectionScale around the bounds' top-left so the raster fills the
    // bounds in authoring space.
    private static void RenderOperation(ImmediateCanvas canvas, RenderNodeOperation op)
    {
        RenderScale scale = op.CorrectionScale;
        if (scale.IsIdentity)
        {
            op.Render(canvas);
            return;
        }

        Matrix scaleMatrix = BuildScaleAroundPivot(scale.ScaleX, scale.ScaleY, op.Bounds.X, op.Bounds.Y);
        using (canvas.PushTransform(scaleMatrix))
        {
            op.Render(canvas);
        }
    }

    private static Matrix BuildScaleAroundPivot(float sx, float sy, float px, float py)
    {
        // T(px,py) * S(sx,sy) * T(-px,-py)
        return new Matrix(sx, 0, 0, sy, px * (1 - sx), py * (1 - sy));
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

        var context = new RenderNodeContext(input);
        var result = node.Process(context);
        if (useRenderCache && !context.IsRenderCacheEnabled)
        {
            node.Cache.ReportRenderCount(0);
        }

        return result;
    }
}
