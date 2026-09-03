namespace Beutl.Graphics.Rendering.Requests;

internal sealed class RenderTargetLease : IDisposable
{
    internal RenderTargetLease(
        RenderTargetLeaseSession session,
        PooledRenderTargetLease pooledLease)
    {
        Session = session;
        PooledLease = pooledLease;
    }

    public RenderTarget Target => PooledLease.Target;

    public bool IsReleased { get; internal set; }

    internal RenderTargetLeaseSession Session { get; }

    internal PooledRenderTargetLease PooledLease { get; }

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
