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

    public static void ClearCache(RenderNode node)
    {
        node.Cache.Invalidate();

        if (node is not ContainerRenderNode containerNode) return;

        foreach (RenderNode item in containerNode.Children)
        {
            ClearCache(item);
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
