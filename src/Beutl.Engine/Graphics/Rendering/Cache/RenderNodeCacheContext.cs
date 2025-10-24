using System.Text.Json.Serialization;
using Beutl.Configuration;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Rendering.Cache;

// TODO: インスタンスのあるクラスである必要はないので、近々削除する
public sealed class RenderNodeCacheContext(RenderScene scene)
{
    internal static readonly ILogger _logger = Log.CreateLogger("RenderNodeCache");

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
            for (int i = 0; i < container.Children.Count; i++)
            {
                RenderNode current = container.Children[i];
                if (!CanCacheRecursive(current))
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
    public void MakeCache(RenderNode node)
    {
        if (!_cacheOptions.IsEnabled)
            return;

        RenderNodeCache cache = node.Cache;
        // ここでのnodeは途中まで、キャッシュしても良い
        // CanCacheRecursive内で再帰呼び出ししているのはすべてキャッシュできる必要がある
        if (CanCacheRecursive(node))
        {
            if (!cache.IsCached)
            {
                CreateDefaultCache(node);
            }
        }
        else if (node is ContainerRenderNode containerNode)
        {
            cache.Invalidate();
            foreach (RenderNode item in containerNode.Children)
            {
                MakeCache(item);
            }
        }
    }

    public void CreateDefaultCache(RenderNode node)
    {
        var processor = new RenderNodeProcessor(node, false);
        var list = processor.RasterizeToRenderTargets();
        int pixels = list.Sum(i =>
        {
            var pr = PixelRect.FromRect(i.Bounds);
            return pr.Width * pr.Height;
        });
        if (!_cacheOptions.Rules.Match(pixels))
            return;

        // nodeの子要素のキャッシュをすべて削除
        ClearCache(node);

        var arr = list.Select(i => (i.RenderTarget, i.Bounds)).ToArray();
        node.Cache.StoreCache(arr);

        _logger.LogInformation("Created cache for node {Node}.", node);

        // 参照のデクリメント
        foreach ((RenderTarget target, Rect _) in arr)
        {
            target.Dispose();
        }
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
