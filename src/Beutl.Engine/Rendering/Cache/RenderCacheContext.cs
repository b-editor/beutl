using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

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
    private RenderCacheOptions _cacheOptions = new();

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

    private void MakeCacheCore(IGraphicNode node, RenderCache cache, IImmediateCanvasFactory factory)
    {
        // nodeの子要素のキャッシュをすべて削除
        ClearCache(node, cache);

        // nodeをキャッシュ
        Rect bounds = (node as ISupportRenderCache)?.TransformBoundsForCache(cache) ?? node.Bounds;
        PixelSize size = new((int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height));
        if (size.Width <= 0 || size.Height <= 0)
            return;

        SKSurface? surface = factory.CreateRenderTarget(size.Width, size.Height);
        if (surface == null)
        {
            _logger.LogWarning("CreateRenderTarget returns null. ({Width}x{Height})", size.Width, size.Height);
            return;
        }

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

        using (var surfaceRef = Ref<SKSurface>.Create(surface))
        {
            cache.StoreCache(surfaceRef, bounds);
        }

        Debug.WriteLine($"[RenderCache:Created] '{node}'");
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
public record RenderCacheOptions(
    bool IsEnabled = true,
    RenderCacheRules Rules = new())
{
    public static readonly RenderCacheOptions Default = new();
    public static readonly RenderCacheOptions Disabled = new();
}

public readonly record struct RenderCacheRules(
    int MaxWidth = int.MaxValue,
    int MaxHeight = int.MaxValue,
    int MinWidth = 0,
    int MinHeight = 0)
{
    public bool Match(PixelSize size)
    {
        return (size.Width <= MaxWidth && size.Height <= MaxHeight)
            || (size.Width >= MinWidth && size.Height >= MinHeight);
    }
}
