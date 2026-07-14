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
        float outputScale = 1f, float maxWorkingScale = float.PositiveInfinity,
        PipelineDiagnostics? diagnostics = null, RenderTargetPool? pool = null,
        RenderIntent? renderIntent = null)
    {
        if (!cacheOptions.IsEnabled)
            return;

        RenderNodeCache cache = node.Cache;
        // ここでのnodeは途中まで、キャッシュしても良い
        // CanCacheRecursive内で再帰呼び出ししているのはすべてキャッシュできる必要がある
        if (CanCacheRecursive(node))
        {
            if (!cache.IsCached && !cache.IsCacheRejected)
            {
                CreateDefaultCache(
                    node, cacheOptions, outputScale, maxWorkingScale, diagnostics, pool, renderIntent);
            }
        }
        else if (node is ContainerRenderNode containerNode)
        {
            cache.Invalidate();
            foreach (RenderNode item in containerNode.Children)
            {
                MakeCache(item, cacheOptions, outputScale, maxWorkingScale, diagnostics, pool, renderIntent);
            }
        }
    }

    private static void NotifyServedFromCache(RenderNode node)
    {
        node.OnServedFromCache();
        if (node is ContainerRenderNode container)
        {
            foreach (RenderNode child in container.Children)
                NotifyServedFromCache(child);
        }
    }

    public static void CreateDefaultCache(RenderNode node, RenderCacheOptions cacheOptions,
        float outputScale = 1f, float maxWorkingScale = float.PositiveInfinity,
        PipelineDiagnostics? diagnostics = null, RenderTargetPool? pool = null,
        RenderIntent? renderIntent = null)
    {
        // Rasterize the cache at the renderer's density under its working-scale ceiling. The diagnostics/pool reach
        // only the pull's effect INTERMEDIATES (RenderNodeContext); the retained cache targets themselves stay
        // non-pooled (RasterizeAt allocates via CreateRenderTarget), so no pooled lease outlives the warm-up frame.
        var processor = new RenderNodeProcessor(
            node, false, outputScale, maxWorkingScale, diagnostics, pool, renderIntent);
        var ops = processor.PullToRoot();

        // Refuse to cache a subtree whose supply density exceeds outputScale: caching would
        // discard the extra detail and silently lower downstream working scales.
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
            var pr = outputScale == 1f ? PixelRect.FromRect(i.Bounds) : PixelRect.FromRect(i.Bounds, outputScale);
            return (long)pr.Width * pr.Height;
        });
        if (!cacheOptions.Rules.Match(pixels))
        {
            // Release rasterized tiles on the reject path to avoid leaking RenderTarget surfaces.
            foreach (var i in list)
                i.RenderTarget.Dispose();
            node.Cache.RejectCache();
            return;
        }

        // nodeの子要素のキャッシュをすべて削除
        ClearCache(node);

        var arr = list.Select(i => (i.RenderTarget, i.Bounds)).ToArray();
        try
        {
            node.Cache.StoreCache(arr, outputScale);

            // From here this subtree replays from node's tiles, so no descendant's Process runs again. Notify the whole
            // subtree so any node holding a cross-frame resource outside its own node cache (an effect node's retained
            // prefix lease) releases it now rather than stranding it until dispose (C10).
            NotifyServedFromCache(node);

            _logger.LogInformation("Created cache for node {Node}.", node);
        }
        catch
        {
            // StoreCache may have retained some or all shallow copies before a later notification fails. Roll the
            // cache back without allowing native cleanup failures to replace the author callback's exception.
            try
            {
                node.Cache.Invalidate();
            }
            catch
            {
            }

            throw;
        }
        finally
        {
            // Drop every warm-up handle even if StoreCache or NotifyServedFromCache throws. Each cache tile owns a
            // shallow copy, so the successful path retains exactly one reference and the failure path retains none.
            foreach ((RenderTarget target, Rect _) in arr)
            {
                try
                {
                    target.Dispose();
                }
                catch
                {
                }
            }
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

    // Normalize Min >= 1, Max >= Min so Match() is never trivially false.
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
