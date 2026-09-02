namespace Beutl.Graphics.Rendering.Requests;

internal interface IRenderCacheLookup
{
    /// <remarks>
    /// One resolver call observes a stable lookup snapshot. Implementations must not change the result for the
    /// same candidate and complete identity until that call returns.
    /// </remarks>
    bool TryGet(
        RenderCacheCandidate candidate,
        RenderOutputCacheIdentity identity,
        out RenderCacheEntry? entry);
}
