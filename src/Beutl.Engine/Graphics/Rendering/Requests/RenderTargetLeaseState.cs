namespace Beutl.Graphics.Rendering.Requests;

internal enum RenderTargetLeaseState : byte
{
    Leased,
    Deferred,
    Released,
    Evicted,
    CacheTransferred,
}
