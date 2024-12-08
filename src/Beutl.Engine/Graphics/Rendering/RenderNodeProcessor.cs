﻿using Beutl.Collections.Pooled;
using Beutl.Media;
using Beutl.Media.Pixel;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

public class RenderNodeProcessor
{
    private readonly IImmediateCanvasFactory _canvasFactory;
    private readonly bool _useRenderCache;

    public RenderNodeProcessor(
        RenderNode root, IImmediateCanvasFactory canvasFactory, bool useRenderCache)
    {
        _canvasFactory = canvasFactory;
        _useRenderCache = useRenderCache;
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

    internal List<(SKSurface Surface, Rect Bounds)> RasterizeToSurface()
    {
        var list = new List<(SKSurface, Rect)>();
        var ops = PullToRoot();
        foreach (var op in ops)
        {
            var rect = PixelRect.FromRect(op.Bounds);
            if (rect.Width <= 0 || rect.Height <= 0) continue;
            SKSurface surface = _canvasFactory.CreateRenderTarget(rect.Width, rect.Height)
                                ?? throw new Exception("surface is null");

            using ImmediateCanvas canvas = _canvasFactory.CreateCanvas(surface, true);

            using (canvas.PushTransform(Matrix.CreateTranslation(-op.Bounds.X, -op.Bounds.Y)))
            {
                op.Render(canvas);
                op.Dispose();
            }

            list.Add((surface, op.Bounds));
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
            using SKSurface? surface = _canvasFactory.CreateRenderTarget(rect.Width, rect.Height)
                                       ?? throw new Exception("surface is null");

            using ImmediateCanvas canvas = _canvasFactory.CreateCanvas(surface, true);

            using (canvas.PushTransform(Matrix.CreateTranslation(-op.Bounds.X, -op.Bounds.Y)))
            {
                op.Render(canvas);
                op.Dispose();
            }

            list.Add(canvas.GetBitmap());
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

        using ImmediateCanvas canvas = _canvasFactory.CreateCanvas(surface, true);
        using (canvas.PushTransform(Matrix.CreateTranslation(-bounds.X, -bounds.Y)))
        {
            foreach (var op in ops)
            {
                op.Render(canvas);
                op.Dispose();
            }
        }

        return canvas.GetBitmap();
    }

    public RenderNodeOperation[] PullToRoot()
    {
        return Pull(Root);
    }

    public RenderNodeOperation[] Pull(RenderNode node)
    {
        if (_useRenderCache && node.Cache is { IsCached: true } cache)
        {
            return cache.UseCache()
                .Select(i => RenderNodeOperation.CreateFromSurface(
                    bounds: i.Bounds,
                    position: i.Bounds.Position,
                    surface: i.Surface))
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
        var result = node.Process(context);
        if (_useRenderCache && !context.IsRenderCacheEnabled)
        {
            node.Cache.ReportRenderCount(0);
        }

        return result;
    }
}
