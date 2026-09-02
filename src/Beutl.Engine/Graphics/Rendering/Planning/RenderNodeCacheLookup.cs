using Beutl.Graphics.Rendering.Cache;

namespace Beutl.Graphics.Rendering;

internal sealed class RenderNodeCacheLookup : IRenderCacheLookup
{
    public static RenderNodeCacheLookup Instance { get; } = new();

    private RenderNodeCacheLookup()
    {
    }

    public bool TryGet(
        RenderCacheCandidate candidate,
        RenderOutputCacheIdentity identity,
        out RenderCacheEntry? entry)
    {
        if (candidate.Cache?.TryGetCachedOutput(identity, out RenderNodeCachedOutput? output) == true)
        {
            entry = new RenderCacheEntry(identity, output!);
            return true;
        }

        entry = null;
        return false;
    }
}
