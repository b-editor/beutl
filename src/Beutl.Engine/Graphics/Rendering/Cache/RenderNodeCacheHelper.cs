using System.Runtime.ExceptionServices;
using System.Text.Json.Serialization;
using Beutl.Configuration;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Rendering.Cache;

public static class RenderNodeCacheHelper
{
    internal static readonly ILogger _logger = Log.CreateLogger("RenderNodeCache");
    [ThreadStatic]
    private static Action<RenderTarget>? s_rejectTargetDisposerForTest;

    internal static void SetRejectTargetDisposerForTest(Action<RenderTarget>? disposer)
        => s_rejectTargetDisposerForTest = disposer;

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
        Exception? failure = null;
        ClearCacheCore(node, ref failure);
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void ClearCacheCore(RenderNode node, ref Exception? failure)
    {
        RenderNode[] children = node is ContainerRenderNode containerNode
            ? [.. containerNode.Children]
            : [];

        try
        {
            node.Cache.Invalidate();
        }
        catch (Exception ex)
        {
            failure ??= ex;
        }

        foreach (RenderNode item in children)
        {
            ClearCacheCore(item, ref failure);
        }
    }

    // 再帰呼び出し
    public static void MakeCache(RenderNode node, RenderCacheOptions cacheOptions,
        RenderIntent renderIntent, float outputScale = 1f, float maxWorkingScale = float.PositiveInfinity,
        PipelineDiagnostics? diagnostics = null,
        RenderPullPurpose pullPurpose = RenderPullPurpose.Frame)
    {
        ValidatePersistentCachePolicy(renderIntent, pullPurpose);
        MakeCache(
            node, cacheOptions, renderIntent, outputScale, maxWorkingScale, diagnostics,
            pool: null, pullPurpose: pullPurpose);
    }

    internal static void MakeCache(RenderNode node, RenderCacheOptions cacheOptions,
        RenderIntent renderIntent, float outputScale, float maxWorkingScale,
        PipelineDiagnostics? diagnostics, RenderTargetPool? pool,
        RenderPullPurpose pullPurpose = RenderPullPurpose.Frame)
    {
        ValidatePersistentCachePolicy(renderIntent, pullPurpose);
        if (!cacheOptions.IsEnabled)
            return;

        RenderNodeCache cache = node.Cache;
        // ここでのnodeは途中まで、キャッシュしても良い
        // CanCacheRecursive内で再帰呼び出ししているのはすべてキャッシュできる必要がある
        if (CanCacheRecursive(node))
        {
            if (!cache.IsCachedFor(renderIntent, pullPurpose)
                && !cache.IsCacheRejectedFor(renderIntent, pullPurpose))
            {
                CreateDefaultCache(
                    node, cacheOptions, renderIntent, outputScale, maxWorkingScale, diagnostics, pool, pullPurpose);
            }
        }
        else if (node is ContainerRenderNode containerNode)
        {
            cache.Invalidate();
            foreach (RenderNode item in containerNode.Children)
            {
                MakeCache(
                    item, cacheOptions, renderIntent, outputScale, maxWorkingScale, diagnostics, pool, pullPurpose);
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
        RenderIntent renderIntent, float outputScale = 1f, float maxWorkingScale = float.PositiveInfinity,
        PipelineDiagnostics? diagnostics = null,
        RenderPullPurpose pullPurpose = RenderPullPurpose.Frame)
    {
        ValidatePersistentCachePolicy(renderIntent, pullPurpose);
        CreateDefaultCache(
            node, cacheOptions, renderIntent, outputScale, maxWorkingScale, diagnostics,
            pool: null, pullPurpose: pullPurpose);
    }

    internal static void CreateDefaultCache(RenderNode node, RenderCacheOptions cacheOptions,
        RenderIntent renderIntent, float outputScale, float maxWorkingScale,
        PipelineDiagnostics? diagnostics, RenderTargetPool? pool,
        RenderPullPurpose pullPurpose = RenderPullPurpose.Frame)
    {
        ValidatePersistentCachePolicy(renderIntent, pullPurpose);
        // Rasterize the cache at the renderer's density under its working-scale ceiling. The diagnostics/pool reach
        // only the pull's effect INTERMEDIATES (RenderNodeContext); the retained cache targets themselves stay
        // non-pooled (RasterizeAt allocates via CreateRenderTarget), so no pooled lease outlives the warm-up frame.
        var processor = new RenderNodeProcessor(
            pool, node, false, renderIntent, outputScale, maxWorkingScale, diagnostics, pullPurpose);
        var ops = processor.PullToRoot();
        bool ownsOperations = true;
        List<(RenderTarget RenderTarget, Rect Bounds)>? list = null;
        Exception? failure = null;
        try
        {
            // Refuse to cache a subtree whose supply density exceeds outputScale: caching would
            // discard the extra detail and silently lower downstream working scales. EffectiveScale
            // is author-provided operation state and may throw, so keep it under the op cleanup guard.
            if (ops.Any(o => !o.EffectiveScale.IsUnbounded && o.EffectiveScale.Value > outputScale))
            {
                node.Cache.RejectCache(renderIntent, pullPurpose);
            }
            else
            {
                list = processor.RasterizeToRenderTargets(ops);
                ownsOperations = false;
                long pixels = list.Sum(i =>
                {
                    var pr = outputScale == 1f
                        ? PixelRect.FromRect(i.Bounds)
                        : PixelRect.FromRect(i.Bounds, outputScale);
                    return (long)pr.Width * pr.Height;
                });
                if (!cacheOptions.Rules.Match(pixels))
                {
                    node.Cache.RejectCache(renderIntent, pullPurpose);
                }
                else
                {
                    // Clear every cached child node. This can invoke native cleanup,
                    // so the newly-rasterized list must already be inside its ownership guard.
                    ClearCache(node);

                    var arr = list.Select(i => (i.RenderTarget, i.Bounds)).ToArray();
                    try
                    {
                        node.Cache.StoreCache(arr, renderIntent, outputScale, pullPurpose);

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
                }
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            if (ownsOperations)
            {
                RenderNodeOperation.DisposeAll(ops, ref failure);
            }

            if (list != null)
                DisposeWarmupTargets(list, ref failure);
        }

        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void DisposeWarmupTargets(
        IEnumerable<(RenderTarget RenderTarget, Rect Bounds)> targets,
        ref Exception? failure)
    {
        foreach ((RenderTarget target, Rect _) in targets)
        {
            try
            {
                (s_rejectTargetDisposerForTest ?? (static current => current.Dispose()))(target);
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }
    }

    private static void ValidatePersistentCachePolicy(
        RenderIntent renderIntent,
        RenderPullPurpose pullPurpose)
    {
        RenderPolicyValidation.Validate(renderIntent, nameof(renderIntent));
        pullPurpose = RenderPolicyValidation.Validate(pullPurpose, nameof(pullPurpose));
        if (pullPurpose != RenderPullPurpose.Frame)
        {
            throw new NotSupportedException(
                "Persistent render-node cache warm-up is frame-only; auxiliary pulls must execute without retaining or replaying frame-cache state.");
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
