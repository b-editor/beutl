using System.Runtime.CompilerServices;
using Beutl.Graphics.Rendering.Cache;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Rendering.V2.Cache;

public sealed class RenderNodeCacheContext : IDisposable
{
    private readonly ILogger<RenderNodeCacheContext> _logger =
        BeutlApplication.Current.LoggerFactory.CreateLogger<RenderNodeCacheContext>();

    private readonly ConditionalWeakTable<RenderNode, RenderNodeCache> _table = [];
    private RenderCacheOptions _cacheOptions = RenderCacheOptions.CreateFromGlobalConfiguration();

    public RenderCacheOptions CacheOptions
    {
        get => _cacheOptions;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_cacheOptions != value)
            {
                Dispose();
            }

            _cacheOptions = value;
        }
    }

    public void RevalidateCache(RenderNode node)
    {
        var cache = GetCache(node);
        if (node is ContainerRenderNode)
        {
            cache.CaptureChildren();
        }

        cache.IncrementRenderCount();
        if (cache.IsCached && !CanCacheRecursive(node))
        {
            cache.Invalidate();
        }
    }

    public RenderNodeCache GetCache(RenderNode node)
    {
        return _table.GetValue(node, key => new RenderNodeCache(key));
    }

    public bool CanCacheRecursive(RenderNode node)
    {
        RenderNodeCache cache = GetCache(node);
        if (!cache.CanCache())
            return false;

        if (node is ContainerRenderNode container)
        {
            if (cache.Children?.Count != container.Children.Count)
                return false;

            for (int i = 0; i < cache.Children.Count; i++)
            {
                WeakReference<RenderNode> capturedRef = cache.Children[i];
                RenderNode current = container.Children[i];
                if (!capturedRef.TryGetTarget(out RenderNode? captured)
                    || !ReferenceEquals(captured, current)
                    || !CanCacheRecursive(current))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // nodeの子要素だけ調べる。node自体は調べない
    // MakeCacheで使う
    public bool CanCacheRecursiveChildrenOnly(RenderNode node)
    {
        if (node is ContainerRenderNode containerNode)
        {
            foreach (RenderNode item in containerNode.Children)
            {
                if (!CanCacheRecursive(item))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public void ClearCache(RenderNode node, RenderNodeCache cache)
    {
        cache.Invalidate();

        if (node is ContainerRenderNode containerNode)
        {
            foreach (RenderNode item in containerNode.Children)
            {
                ClearCache(item);
            }
        }
    }

    public void ClearCache(RenderNode node)
    {
        if (_table.TryGetValue(node, out RenderNodeCache? cache))
        {
            cache.Invalidate();
        }

        if (node is ContainerRenderNode containerNode)
        {
            foreach (RenderNode item in containerNode.Children)
            {
                ClearCache(item);
            }
        }
    }

    // 再帰呼び出し
    public void MakeCache(RenderNode node, IImmediateCanvasFactory factory)
    {
        if (!_cacheOptions.IsEnabled)
            return;

        RenderNodeCache cache = GetCache(node);
        // ここでのnodeは途中まで、キャッシュしても良い
        // CanCacheRecursive内で再帰呼び出ししているのはすべてキャッシュできる必要がある
        if (cache.CanCache() && CanCacheRecursiveChildrenOnly(node))
        {
            if (!cache.IsCached)
            {
                CreateDefaultCache(node, cache, factory);
            }
        }
        else if (node is ContainerRenderNode containerNode)
        {
            cache.Invalidate();
            foreach (RenderNode item in containerNode.Children)
            {
                MakeCache(item, factory);
            }
        }
    }

    public void CreateDefaultCache(RenderNode node, RenderNodeCache cache, IImmediateCanvasFactory factory)
    {
        // TODO: RenderNodeのキャッシュを作成する
        // Rect bounds = node.Bounds;
        // //bounds = bounds.Inflate(5);
        // PixelRect boundsInPixels = PixelRect.FromRect(bounds);
        // PixelSize size = boundsInPixels.Size;
        // if (!_cacheOptions.Rules.Match(size))
        //     return;
        //
        // // nodeの子要素のキャッシュをすべて削除
        // ClearCache(node, cache);
        //
        // // nodeをキャッシュ
        // SKSurface? surface = factory.CreateRenderTarget(size.Width, size.Height);
        // if (surface == null)
        // {
        //     _logger.LogWarning("CreateRenderTarget returns null. ({Width}x{Height})", size.Width, size.Height);
        //     return;
        // }
        //
        // using (ImmediateCanvas canvas = factory.CreateCanvas(surface, true))
        // {
        //     using (canvas.PushTransform(Matrix.CreateTranslation(-boundsInPixels.X, -boundsInPixels.Y)))
        //     {
        //         node.Render(canvas);
        //     }
        // }
        //
        // using (var surfaceRef = Ref<SKSurface>.Create(surface))
        // {
        //     cache.StoreCache(surfaceRef, boundsInPixels.ToRect(1));
        // }
        //
        // Debug.WriteLine($"[RenderCache:Created] '{node}'");
    }

    public void Dispose()
    {
        Clear();
    }

    public void Clear()
    {
        foreach (KeyValuePair<RenderNode, RenderNodeCache> item in _table)
        {
            item.Value.Dispose();
        }

        _table.Clear();
    }
}
