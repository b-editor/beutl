using Beutl.Collections.Pooled;
using Beutl.Graphics.Rendering.V2.Cache;
using Beutl.Media;
using Beutl.Media.Pixel;
using SkiaSharp;

namespace Beutl.Graphics.Rendering.V2;

public class RenderNodeProcessor
{
    private readonly IImmediateCanvasFactory _canvasFactory;
    private readonly RenderNodeCacheContext? _cacheContext;

    public RenderNodeProcessor(
        RenderNode root, IImmediateCanvasFactory canvasFactory,
        RenderNodeCacheContext? cacheContext)
    {
        _canvasFactory = canvasFactory;
        _cacheContext = cacheContext;
        Root = root;
    }

    public RenderNode Root { get; set; }

    public void Render(ImmediateCanvas canvas)
    {
        var ops = PullToRoot();
        foreach (var op in ops)
        {
            op.Render(canvas);
            op.Dispose();
        }
    }

    public List<Bitmap<Bgra8888>> Rasterize()
    {
        var list = new List<Bitmap<Bgra8888>>();
        var ops = PullToRoot();
        foreach (var op in ops)
        {
            var rect = PixelRect.FromRect(op.Bounds);
            using SKSurface? surface = _canvasFactory.CreateRenderTarget(rect.Width, rect.Height)
                                       ?? throw new Exception("surface is null");

            using ImmediateCanvas icanvas = _canvasFactory.CreateCanvas(surface, true);

            using (icanvas.PushTransform(Matrix.CreateTranslation(-op.Bounds.X, -op.Bounds.Y)))
            {
                op.Render(icanvas);
                op.Dispose();
            }

            list.Add(icanvas.GetBitmap());
        }

        return list;
    }

    public Bitmap<Bgra8888> RasterizeAndConcat()
    {
        var ops = PullToRoot();
        var bounds = ops.Aggregate(Rect.Empty, (a, n) => a.Union(n.Bounds));
        var rect = PixelRect.FromRect(bounds);
        using SKSurface surface = _canvasFactory.CreateRenderTarget(rect.Width, rect.Height)
                                  ?? throw new Exception("surface is null");

        using ImmediateCanvas icanvas = _canvasFactory.CreateCanvas(surface, true);
        foreach (var op in ops)
        {
            op.Render(icanvas);
            op.Dispose();
        }

        return icanvas.GetBitmap();
    }

    public RenderNodeOperation[] PullToRoot()
    {
        return Pull(Root);
    }

    public RenderNodeOperation[] Pull(RenderNode node)
    {
        if (_cacheContext?.GetCache(node) is { IsCached: true } cache)
        {
            return cache.UseCache()
                .Select(i => RenderNodeOperation.CreateLambda(
                    bounds: i.Bounds,
                    render: canvas => canvas.DrawSurface(i.Surface.Value, i.Bounds.Position),
                    onDispose: () => i.Surface.Dispose()))
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

        var context = new RenderNodeContext(_canvasFactory, input);
        return node.Process(context);
    }
}
