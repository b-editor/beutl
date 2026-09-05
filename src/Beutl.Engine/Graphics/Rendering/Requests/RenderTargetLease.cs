using Beutl.Media;

namespace Beutl.Graphics.Rendering.Requests;

/// <summary>One exclusive hold on a target owned by a renderer's pool.</summary>
internal sealed class RenderTargetLease : IDisposable
{
    internal RenderTargetLease(
        RenderTargetLeaseSession session,
        RenderTargetPool.TargetSlot slot)
    {
        Session = session;
        Slot = slot;
    }

    public RenderTarget Target
    {
        get
        {
            Session.Pool.VerifyLease(this);
            return Slot.Target;
        }
    }

    public PixelSize DeviceSize
    {
        get
        {
            Session.Pool.VerifyLease(this);
            return Slot.Size;
        }
    }

    public RenderTargetLeaseState State { get; internal set; } = RenderTargetLeaseState.Leased;

    public bool IsReleased => State != RenderTargetLeaseState.Leased;

    internal RenderTargetLeaseSession Session { get; }

    internal RenderTargetPool.TargetSlot Slot { get; }

    internal Exception? ReleaseFailure { get; set; }

    public void Dispose()
    {
        Session.Release(this);
    }

    internal void ReleaseForBackendReuse()
    {
        Session.ReleaseForBackendReuse(this);
    }

    public RenderTarget TransferToAcceptedCache()
        => Session.TransferToAcceptedCache(this);
}
