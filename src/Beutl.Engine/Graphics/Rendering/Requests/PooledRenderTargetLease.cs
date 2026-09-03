using Beutl.Media;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed class PooledRenderTargetLease : IDisposable
{
    internal PooledRenderTargetLease(
        RenderTargetPool pool,
        RenderTargetPoolRequest request,
        RenderTargetPool.TargetSlot slot,
        long generation)
    {
        Pool = pool;
        Request = request;
        Slot = slot;
        Generation = generation;
    }

    public RenderTarget Target
    {
        get
        {
            Pool.VerifyLease(this);
            return Slot.Target;
        }
    }

    public PixelSize DeviceSize
    {
        get
        {
            Pool.VerifyLease(this);
            return Slot.Size;
        }
    }

    public long Generation { get; }

    public PooledRenderTargetLeaseState State { get; internal set; } = PooledRenderTargetLeaseState.Leased;

    internal RenderTargetPool Pool { get; }

    internal RenderTargetPoolRequest Request { get; }

    internal RenderTargetPool.TargetSlot Slot { get; }

    public RenderTarget TransferToAcceptedCache()
        => Pool.TransferToAcceptedCache(this);

    internal void DeferRelease()
        => Pool.DeferRelease(this);

    internal void CompleteDeferredRelease()
        => Pool.CompleteDeferredRelease(this);

    public void Dispose()
        => Pool.Release(this);
}
