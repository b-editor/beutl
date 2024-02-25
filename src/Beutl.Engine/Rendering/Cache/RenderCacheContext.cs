using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

using Beutl.Configuration;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;

using Microsoft.Extensions.Logging;

using SkiaSharp;

namespace Beutl.Rendering.Cache;

public sealed class RenderCacheContext : IDisposable
{
    private readonly ILogger<RenderCacheContext> _logger = BeutlApplication.Current.LoggerFactory.CreateLogger<RenderCacheContext>();
    private readonly ConditionalWeakTable<IGraphicNode, RenderCache> _table = [];
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

    public RenderCache GetCache(IGraphicNode node)
    {
        return _table.GetValue(node, key => new RenderCache(key));
    }

    public bool CanCacheRecursive(IGraphicNode node)
    {
        RenderCache cache = GetCache(node);
        if (!cache.CanCache())
            return false;

        if (node is ContainerNode container)
        {
            if (cache.Children?.Count != container.Children.Count)
                return false;

            for (int i = 0; i < cache.Children.Count; i++)
            {
                WeakReference<IGraphicNode> capturedRef = cache.Children[i];
                IGraphicNode current = container.Children[i];
                if (!capturedRef.TryGetTarget(out IGraphicNode? captured)
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
                ClearCache(item);
            }
        }
    }

    public void ClearCache(IGraphicNode node)
    {
        if (_table.TryGetValue(node, out RenderCache? cache))
        {
            cache.Invalidate();
        }

        if (node is ContainerNode containerNode)
        {
            foreach (IGraphicNode item in containerNode.Children)
            {
                ClearCache(item);
            }
        }
    }

    // 再帰呼び出し
    public void MakeCache(IGraphicNode node, IImmediateCanvasFactory factory)
    {
        if (!_cacheOptions.IsEnabled)
            return;

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

    public void CreateDefaultCache(IGraphicNode node, RenderCache cache, IImmediateCanvasFactory factory)
    {
        Rect bounds = node.Bounds;
        //bounds = bounds.Inflate(5);
        PixelRect boundsInPixels = PixelRect.FromRect(bounds);
        PixelSize size = boundsInPixels.Size;
        if (!_cacheOptions.Rules.Match(size))
            return;

        // nodeの子要素のキャッシュをすべて削除
        ClearCache(node, cache);

        // nodeをキャッシュ
        SKSurface? surface = factory.CreateRenderTarget(size.Width, size.Height);
        if (surface == null)
        {
            _logger.LogWarning("CreateRenderTarget returns null. ({Width}x{Height})", size.Width, size.Height);
            return;
        }

        using (ImmediateCanvas canvas = factory.CreateCanvas(surface, true))
        {
            using (canvas.PushTransform(Matrix.CreateTranslation(-boundsInPixels.X, -boundsInPixels.Y)))
            {
                node.Render(canvas);
            }
        }

        using (var surfaceRef = Ref<SKSurface>.Create(surface))
        {
            cache.StoreCache(surfaceRef, boundsInPixels.ToRect(1));
        }

        Debug.WriteLine($"[RenderCache:Created] '{node}'");
    }

    private void MakeCacheCore(IGraphicNode node, RenderCache cache, IImmediateCanvasFactory factory)
    {
        if (node is ISupportRenderCache supportRenderCache)
        {
            supportRenderCache.CreateCache(factory, cache, this);
        }
        else
        {
            CreateDefaultCache(node, cache, factory);
        }
    }

    public void Dispose()
    {
        Clear();
    }

    public void Clear()
    {
        foreach (KeyValuePair<IGraphicNode, RenderCache> item in _table)
        {
            item.Value.Dispose();
        }

        _table.Clear();
    }
}

[JsonSerializable(typeof(RenderCacheOptions))]
public record RenderCacheOptions(bool IsEnabled, RenderCacheRules Rules)
{
    public static readonly RenderCacheOptions Default = new(true, RenderCacheRules.Default);
    public static readonly RenderCacheOptions Disabled = new(false, RenderCacheRules.Default);

    public static RenderCacheOptions CreateFromGlobalConfiguration()
    {
        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;
        return new RenderCacheOptions(
            config.IsNodeCacheEnabled,
            new RenderCacheRules(config.NodeCacheMaxPixels, config.NodeCacheMinPixels));
    }
}

public readonly record struct RenderCacheRules(int MaxPixels, int MinPixels)
{
    public static readonly RenderCacheRules Default = new(1000 * 1000, 1);

    public bool Match(PixelSize size)
    {
        int count = size.Width * size.Height;
        return MinPixels <= count && count <= MaxPixels;
    }
}
