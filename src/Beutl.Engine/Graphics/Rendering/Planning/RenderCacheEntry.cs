namespace Beutl.Graphics.Rendering;

/// <summary>
/// An acquired cache entry. Payload ownership remains defined by the lookup implementation; the resolver only
/// retains this opaque handle and never reads or disposes the payload.
/// </summary>
internal sealed class RenderCacheEntry
{
    public RenderCacheEntry(RenderOutputCacheIdentity identity, object payload)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(payload);
        Identity = identity;
        Payload = payload;
    }

    public RenderOutputCacheIdentity Identity { get; }

    public object Payload { get; }
}
