namespace Beutl.Graphics.Rendering.Requests;

internal enum RenderTargetLeaseState : byte
{
    Leased,
    ReleaseFailed,
    Deferred,
    Released,
    Evicted,
    CacheTransferred,
}
