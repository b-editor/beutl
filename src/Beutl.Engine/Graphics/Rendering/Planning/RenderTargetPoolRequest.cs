using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

internal sealed class RenderTargetPoolRequest : IDisposable
{
    private readonly RenderTargetPool _pool;
    private readonly List<PooledRenderTargetLease> _leases = [];
    private readonly List<Exception> _cleanupFailures = [];

    internal RenderTargetPoolRequest(
        RenderTargetPool pool,
        object contextIdentity,
        long contextGeneration,
        nint? expectedContextHandle,
        RenderTarget? externalTarget)
    {
        _pool = pool;
        ContextIdentity = contextIdentity;
        ContextGeneration = contextGeneration;
        ExpectedContextHandle = expectedContextHandle;
        ExternalTarget = externalTarget;
        ExternalSurface = externalTarget?.RawValue;
    }

    public bool IsDisposed { get; private set; }

    public IReadOnlyList<Exception> CleanupFailures => _cleanupFailures;

    internal object ContextIdentity { get; }

    internal long ContextGeneration { get; }

    internal nint? ExpectedContextHandle { get; }

    internal RenderTarget? ExternalTarget { get; }

    internal SKSurface? ExternalSurface { get; }

    public PooledRenderTargetLease Acquire(PixelSize deviceSize)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return _pool.Acquire(this, deviceSize);
    }

    public bool TryAcquire(
        PixelSize deviceSize,
        [NotNullWhen(true)] out PooledRenderTargetLease? lease,
        bool clearContents = true)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return _pool.TryAcquire(this, deviceSize, out lease, clearContents);
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        ExceptionDispatchInfo? primary = null;
        try
        {
            for (int index = _leases.Count - 1; index >= 0; index--)
            {
                PooledRenderTargetLease lease = _leases[index];
                if (lease.State == PooledRenderTargetLeaseState.Leased)
                {
                    try
                    {
                        _pool.Release(lease);
                    }
                    catch (Exception ex)
                    {
                        primary ??= ExceptionDispatchInfo.Capture(ex);
                        try
                        {
                            _pool.EvictAfterReleaseFailure(lease);
                        }
                        catch (Exception cleanup)
                        {
                            primary ??= ExceptionDispatchInfo.Capture(cleanup);
                        }
                    }
                }
            }
        }
        finally
        {
            _pool.EndRequest(this);
        }

        primary?.Throw();
    }

    internal void Register(PooledRenderTargetLease lease)
    {
        _leases.Add(lease);
    }

    internal void RecordCleanupFailure(Exception exception)
    {
        _cleanupFailures.Add(exception);
    }
}
