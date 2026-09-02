namespace Beutl.Graphics.Rendering.Requests;

internal enum PooledRenderTargetLeaseState : byte
{
    Leased,
    Deferred,
    Available,
    Evicted,
    CacheTransferred,
}
