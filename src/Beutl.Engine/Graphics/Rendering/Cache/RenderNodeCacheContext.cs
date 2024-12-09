using System.Diagnostics;
using System.Text.Json.Serialization;
using Beutl.Configuration;
using Beutl.Media;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Rendering.Cache;

// TODO: インスタンスのあるクラスである必要はないので、近々削除する
public sealed class RenderNodeCacheContext(RenderScene scene)
{
    private readonly ILogger<RenderNodeCacheContext> _logger =
        BeutlApplication.Current.LoggerFactory.CreateLogger<RenderNodeCacheContext>();

    private RenderCacheOptions _cacheOptions = RenderCacheOptions.CreateFromGlobalConfiguration();

    public RenderCacheOptions CacheOptions
    {
        get => _cacheOptions;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            scene.ClearCache();
            _cacheOptions = value;
        }
    }

    public static bool CanCacheRecursive(RenderNode node)
    {
        RenderNodeCache cache = node.Cache;
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
    public static bool CanCacheRecursiveChildrenOnly(RenderNode node)
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

    public static void ClearCache(RenderNode node, RenderNodeCache cache)
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

    public static void ClearCache(RenderNode node)
    {
        node.Cache.Invalidate();

        if (node is not ContainerRenderNode containerNode) return;

        foreach (RenderNode item in containerNode.Children)
        {
            ClearCache(item);
        }
    }

    // 再帰呼び出し
    public void MakeCache(RenderNode node, IImmediateCanvasFactory factory)
    {
        if (!_cacheOptions.IsEnabled)
            return;

        RenderNodeCache cache = node.Cache;
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
        var processor = new RenderNodeProcessor(node, factory, false);
        var list = processor.RasterizeToSurface();
        int pixels = list.Sum(i =>
        {
            var pr = PixelRect.FromRect(i.Bounds);
            return pr.Width * pr.Height;
        });
        if (!_cacheOptions.Rules.Match(pixels))
            return;

        // nodeの子要素のキャッシュをすべて削除
        ClearCache(node, cache);

        var arr = list.Select(i => (i.Surface, i.Bounds)).ToArray();
        cache.StoreCache(arr);

        Debug.WriteLine($"[RenderCache:Created] '{node}'");
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

    public bool Match(int pixels)
    {
        return MinPixels <= pixels && pixels <= MaxPixels;
    }
}
