using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Rendering.Cache;

internal static class RenderNodeCacheHelper
{
    internal static readonly ILogger _logger = Log.CreateLogger("RenderNodeCache");

    internal static RenderNodeCacheLifecycle BeginLifecycle(RenderNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return RenderNodeCacheLifecycle.Create(root);
    }

    /// <summary>Resets the cache of <paramref name="node"/> and of every node it owns.</summary>
    /// <remarks>
    /// Ownership, not <see cref="RenderNode.ChildNodes"/>: a merely referenced node is shared with other
    /// live entries, so tearing down one holder must not drop caches the others still rely on.
    /// </remarks>
    internal static void ClearOwnedCaches(RenderNode node)
    {
        node.Cache.Reset();

        if (node is not ContainerRenderNode containerNode) return;

        foreach (RenderNode item in containerNode.Children)
        {
            ClearOwnedCaches(item);
        }
    }
}
