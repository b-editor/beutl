namespace Beutl.Graphics.Rendering;

internal enum PooledRenderTargetLeaseState : byte
{
    Leased,
    Deferred,
    Available,
    Evicted,
    CacheTransferred,
}
