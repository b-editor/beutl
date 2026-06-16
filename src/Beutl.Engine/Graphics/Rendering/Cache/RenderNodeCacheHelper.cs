using System.Text.Json.Serialization;
using Beutl.Configuration;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Rendering.Cache;

public static class RenderNodeCacheHelper
{
    internal static readonly ILogger _logger = Log.CreateLogger("RenderNodeCache");

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
    public static void MakeCache(RenderNode node, RenderCacheOptions cacheOptions,
        float outputScale = 1f, float maxWorkingScale = float.PositiveInfinity)
    {
        if (!cacheOptions.IsEnabled)
            return;

        RenderNodeCache cache = node.Cache;
        // ここでのnodeは途中まで、キャッシュしても良い
        // CanCacheRecursive内で再帰呼び出ししているのはすべてキャッシュできる必要がある
        if (CanCacheRecursive(node))
        {
            // Skip a subtree CreateDefaultCache already refused: it stays uncached and the render count keeps
            // climbing, so without the rejected flag we would re-pull + re-reject it every frame.
            if (!cache.IsCached && !cache.IsCacheRejected)
            {
                CreateDefaultCache(node, cacheOptions, outputScale, maxWorkingScale);
            }
        }
        else if (node is ContainerRenderNode containerNode)
        {
            cache.Invalidate();
            foreach (RenderNode item in containerNode.Children)
            {
                MakeCache(item, cacheOptions, outputScale, maxWorkingScale);
            }
        }
    }

    public static void CreateDefaultCache(RenderNode node, RenderCacheOptions cacheOptions,
        float outputScale = 1f, float maxWorkingScale = float.PositiveInfinity)
    {
        // feature 003 (FR-020/FR-037): rasterize the cache at the renderer's density under its working-scale
        // ceiling. The old default-scale processor baked density-1 tiles regardless of render scale and let
        // high-density sources escape the FR-037 ceiling during cache creation.
        var processor = new RenderNodeProcessor(node, false, outputScale, maxWorkingScale);
        var ops = processor.PullToRoot();

        // feature 003 (FR-018, I4 cache-density-collapse fix): the cache rasterizes every op at outputScale and
        // re-tags the replayed tile EffectiveScale.At(outputScale). A subtree whose output carries a concrete
        // supply density ABOVE outputScale (e.g. a transform-densified At(4) source on a 1080 timeline) would
        // have that detail DISCARDED into the outputScale tile, so every downstream effect resolving its working
        // scale from this input would silently drop from w = supply to w = outputScale once the cache warms — a
        // non-transparent FR-018 violation. So we refuse to cache such a subtree; it keeps rendering uncached at
        // its true supply density. The proper fix is a per-tile-density cache (rasterize each tile at its own
        // working scale, store the density), deferred as T025.
        //
        // The symmetric BELOW-output case (an enlarged At(0.5) bitmap) is already transparent: ResolveWorkingScale
        // floors the working scale at outputScale (w = max(s_out, supply)), so the input resolves to w = outputScale
        // cached or not — the tags agree, no temporal snap when the cache warms. The one residual non-transparency
        // is a re-rasterizable VECTOR subtree (text/shape) cached as a 1× tile then sampled into a HIGHER-w buffer
        // raised by a DENSER concrete SIBLING under a shared effect: the frozen tile up-scales where uncached vector
        // would re-rasterize crisply. Rejecting all vector caching to close that narrow case would gut the cache's
        // primary purpose (static text/shapes, the dominant cacheable content); the per-tile-density cache (T025) is
        // the real fix. So we reject only the above-output case here and leave vector caching on.
        if (ops.Any(o => !o.EffectiveScale.IsUnbounded && o.EffectiveScale.Value > outputScale))
        {
            foreach (var op in ops)
                op.Dispose();
            node.Cache.RejectCache();
            return;
        }

        var list = processor.RasterizeToRenderTargets(ops);
        long pixels = list.Sum(i =>
        {
            // Budget the ACTUAL stored pixels (density-scaled), matching the allocated tile size.
            var pr = outputScale == 1f ? PixelRect.FromRect(i.Bounds) : PixelRect.FromRect(i.Bounds, outputScale);
            return (long)pr.Width * pr.Height;
        });
        if (!cacheOptions.Rules.Match(pixels))
        {
            // The rasterized tiles are caller-owned; release them on the reject path too, or every
            // (density-scaled) RenderTarget surface leaks until finalization — amplified under supersampled
            // export where each tile is ceil(bounds × outputScale) device px and the budget rejects more often.
            foreach (var i in list)
                i.RenderTarget.Dispose();
            node.Cache.RejectCache();
            return;
        }

        // nodeの子要素のキャッシュをすべて削除
        ClearCache(node);

        var arr = list.Select(i => (i.RenderTarget, i.Bounds)).ToArray();
        node.Cache.StoreCache(arr, outputScale);

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
            RenderCacheRules.Create(config.NodeCacheMaxPixels, config.NodeCacheMinPixels));
    }
}

public readonly record struct RenderCacheRules(int MaxPixels, int MinPixels)
{
    public static readonly RenderCacheRules Default = new(1000 * 1000, 1);

    // The settings UI edits Min/Max independently, so normalize the cross-field constraint here
    // (Min >= 1, Max >= Min); otherwise Min > Max makes Match() always false and disables caching.
    public static RenderCacheRules Create(int maxPixels, int minPixels)
    {
        int min = Math.Max(1, minPixels);
        int max = Math.Max(min, maxPixels);
        return new RenderCacheRules(max, min);
    }

    public bool Match(PixelSize size)
    {
        long count = (long)size.Width * size.Height;
        return MinPixels <= count && count <= MaxPixels;
    }

    public bool Match(long pixels)
    {
        return MinPixels <= pixels && pixels <= MaxPixels;
    }
}
