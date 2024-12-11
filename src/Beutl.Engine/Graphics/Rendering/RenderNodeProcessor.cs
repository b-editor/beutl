using Beutl.Collections.Pooled;
using Beutl.Media;
using Beutl.Media.Pixel;

namespace Beutl.Graphics.Rendering;

public class RenderNodeProcessor(RenderNode root, bool useRenderCache)
{
    public RenderNode Root { get; } = root;

    public void Render(ImmediateCanvas canvas)
    {
        var ops = PullToRoot();
        foreach (var op in ops)
        {
            op.Render(canvas);
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

            using (canvas.PushTransform(Matrix.CreateTranslation(-op.Bounds.X, -op.Bounds.Y)))
            {
                op.Render(canvas);
                op.Dispose();
            }

            list.Add((renderTarget, op.Bounds));
        }

        return list;
    }

    public List<Bitmap<Bgra8888>> Rasterize()
    {
        var list = new List<Bitmap<Bgra8888>>();
        var ops = PullToRoot();
        foreach (var op in ops)
        {
            var rect = PixelRect.FromRect(op.Bounds);
            using var renderTarget = RenderTarget.Create(rect.Width, rect.Height)
                                     ?? throw new Exception("RenderTarget is null");

            using var canvas = new ImmediateCanvas(renderTarget);

            using (canvas.PushTransform(Matrix.CreateTranslation(-op.Bounds.X, -op.Bounds.Y)))
            {
                op.Render(canvas);
                op.Dispose();
            }

            list.Add(renderTarget.Snapshot());
        }

        return list;
    }

    public Bitmap<Bgra8888> RasterizeAndConcat()
    {
        var ops = PullToRoot();
        var bounds = ops.Aggregate(Rect.Empty, (a, n) => a.Union(n.Bounds));
        var rect = PixelRect.FromRect(bounds);
        using var renderTarget =
            RenderTarget.Create(rect.Width, rect.Height) ?? throw new Exception("RenderTarget is null");
        using var canvas = new ImmediateCanvas(renderTarget);
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
