using System.Diagnostics;
using System.Runtime.CompilerServices;

using Beutl.Collections.Pooled;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media.Source;

using SkiaSharp;

namespace Beutl.Rendering.Cache;

public sealed class RenderCacheContext : IDisposable
{
    private readonly ConditionalWeakTable<IGraphicNode, RenderCache> _table = new();

    public RenderCache GetCache(IGraphicNode node)
    {
        return _table.GetValue(node, key => new RenderCache(key));
    }

    public bool CanCacheRecursive(IGraphicNode node)
    {
        RenderCache cache = GetCache(node);
        if (!cache.CanCache())
            return false;

        if (node is ContainerNode containerNode)
        {
            foreach (IGraphicNode item in containerNode.Children)
            {
                if (!CanCacheRecursive(item))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // nodeの子要素だけ調べる。node自体は調べない
    // MakeCacheで使う
    public bool CanCacheRecursiveChildrenOnly(IGraphicNode node)
    {
        if (node is ContainerNode containerNode)
        {
            foreach (IGraphicNode item in containerNode.Children)
            {
                if (!CanCacheRecursive(item))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public void ClearCache(IGraphicNode node, RenderCache cache)
    {
        cache.Invalidate();

        if (node is ContainerNode containerNode)
        {
            foreach (IGraphicNode item in containerNode.Children)
            {
                ClearCache(item, GetCache(item));
            }
        }
    }

    // 再帰呼び出しだらけ
    // O(N^2) ???
    public void MakeCache(IGraphicNode node, IImmediateCanvasFactory factory)
    {
        RenderCache cache = GetCache(node);
        // ここでのnodeは途中まで、キャッシュしても良い
        // CanCacheRecursive内で再帰呼び出ししているのはすべてキャッシュできる必要がある
        if (cache.CanCacheBoundary() && CanCacheRecursiveChildrenOnly(node))
        {
            if (!cache.IsCached)
            {
                MakeCacheCore(node, cache, factory);
            }
        }
        else if (node is ContainerNode containerNode)
        {
            cache.Invalidate();
            foreach (IGraphicNode item in containerNode.Children)
            {
                MakeCache(item, factory);
            }
        }
    }

    public void MakeCacheCore(IGraphicNode node, RenderCache cache, IImmediateCanvasFactory factory)
    {
        // nodeの子要素のキャッシュをすべて削除
        ClearCache(node, cache);

        // nodeをキャッシュ
        Rect bounds = (node as ISupportRenderCache)?.TransformBoundsForCache(cache) ?? node.Bounds;
        SKSurface surface = factory.CreateRenderTarget((int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height));

        using (ImmediateCanvas canvas = factory.CreateCanvas(surface, true))
        {
            using (canvas.PushTransform(Matrix.CreateTranslation(-bounds.X, -bounds.Y)))
            {
                if (node is ISupportRenderCache supportRenderCache)
                {
                    supportRenderCache.RenderForCache(canvas, cache);
                }
                else
                {
                    node.Render(canvas);
                }
            }
        }

        cache.StoreCache(Ref<SKSurface>.Create(surface), bounds);

        Debug.WriteLine($"[RenderCache:Created] '{node}'");
    }

    public void Dispose()
    {
        foreach (KeyValuePair<IGraphicNode, RenderCache> item in _table)
        {
            item.Value.Dispose();
        }

        _table.Clear();
    }
}
