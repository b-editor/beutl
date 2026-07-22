using System.Runtime.ExceptionServices;

using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Compatibility-facing adapter over the renderer-lifetime target pool. Request code keeps the
/// original lease vocabulary while released targets remain available for exact-size reuse until
/// the owning renderer is disposed.
/// </summary>
internal sealed class RenderTargetLeaseRegistry : IDisposable
{
    private readonly RenderTargetPool _pool;
    private RenderTargetLeaseSession? _activeSession;
    private bool _disposed;

    public RenderTargetLeaseRegistry(IRenderTargetFactory? factory)
    {
        _pool = new RenderTargetPool(factory);
    }

    public RenderTargetPoolStatistics Statistics => _pool.Statistics;

    public RenderTargetLeaseSession BeginSession(RenderIntent intent, RenderTarget? externalTarget = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_activeSession is not null)
        {
            throw new InvalidOperationException(
                "Concurrent render-target allocation sessions on one renderer are unsupported.");
        }

        RenderTargetPoolRequest request = _pool.BeginRequest(externalTarget);
        var session = new RenderTargetLeaseSession(this, request, intent, externalTarget);
        _activeSession = session;
        return session;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ExceptionDispatchInfo? primary = null;
        try
        {
            _activeSession?.Dispose();
        }
        catch (Exception ex)
        {
            primary = ExceptionDispatchInfo.Capture(ex);
        }

        _activeSession = null;
        try
        {
            _pool.Dispose();
        }
        catch (Exception ex)
        {
            primary ??= ExceptionDispatchInfo.Capture(ex);
        }

        primary?.Throw();
    }

    internal RenderTargetLease Acquire(RenderTargetLeaseSession session, PixelSize deviceSize)
    {
        VerifyActive(session);
        PooledRenderTargetLease pooled = session.Request.Acquire(deviceSize);
        var lease = new RenderTargetLease(session, pooled);
        session.Register(lease);
        return lease;
    }

    internal void Release(RenderTargetLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (lease.IsReleased)
            return;

        lease.IsReleased = true;
        try
        {
            lease.PooledLease.Dispose();
        }
        catch (Exception ex)
        {
            lease.Session.RecordCleanupFailure(ex);
        }
    }

    internal RenderTarget TransferToAcceptedCache(RenderTargetLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        VerifyActive(lease.Session);
        if (lease.IsReleased)
            throw new InvalidOperationException("The render-target lease has already been discharged.");

        RenderTarget target = lease.PooledLease.TransferToAcceptedCache();
        lease.IsReleased = true;
        return target;
    }

    internal void EndSession(RenderTargetLeaseSession session)
    {
        if (ReferenceEquals(_activeSession, session))
            _activeSession = null;
    }

    private void VerifyActive(RenderTargetLeaseSession session)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(session);
        if (!ReferenceEquals(_activeSession, session) || session.IsDisposed)
            throw new InvalidOperationException("The render-target allocation session is no longer active.");
    }
}

internal sealed class RenderTargetLeaseSession : IDisposable
{
    private readonly RenderTargetLeaseRegistry _registry;
    private readonly List<RenderTargetLease> _leases = [];
    private readonly List<Exception> _cleanupFailures = [];

    internal RenderTargetLeaseSession(
        RenderTargetLeaseRegistry registry,
        RenderTargetPoolRequest request,
        RenderIntent intent,
        RenderTarget? externalTarget)
    {
        _registry = registry;
        Request = request;
        Intent = intent;
        ExternalTarget = externalTarget;
    }

    public RenderIntent Intent { get; }

    public bool IsDisposed { get; private set; }

    internal RenderTargetPoolRequest Request { get; }

    internal RenderTarget? ExternalTarget { get; }

    internal IReadOnlyList<Exception> CleanupFailures
        => _cleanupFailures.Concat(Request.CleanupFailures).ToArray();

    internal RenderTargetPoolStatistics PoolStatistics => _registry.Statistics;

    internal RenderCacheDeviceContextIdentity CacheDeviceContextIdentity
        => new(
            _registry,
            new RenderTargetCacheContextIdentity(
                Request.ContextIdentity,
                Request.ContextGeneration));

    public RenderTargetLease Acquire(PixelSize deviceSize)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return _registry.Acquire(this, deviceSize);
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        for (int index = _leases.Count - 1; index >= 0; index--)
            _registry.Release(_leases[index]);
        Request.Dispose();
        _registry.EndSession(this);
    }

    public void ThrowIfCleanupFailed()
    {
        Exception[] failures = [.. CleanupFailures];
        if (failures.Length == 0)
            return;
        if (failures.Length == 1)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();

        throw new AggregateException(
            "One or more render targets failed to discharge.",
            failures);
    }

    internal void Register(RenderTargetLease lease)
    {
        _leases.Add(lease);
    }

    internal void RecordCleanupFailure(Exception exception)
    {
        _cleanupFailures.Add(exception);
    }

    internal void Release(RenderTargetLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!ReferenceEquals(lease.Session, this))
            throw new InvalidOperationException("The render-target lease belongs to a different allocation session.");
        _registry.Release(lease);
    }

    internal RenderTarget TransferToAcceptedCache(RenderTargetLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!ReferenceEquals(lease.Session, this))
            throw new InvalidOperationException("The render-target lease belongs to a different allocation session.");
        return _registry.TransferToAcceptedCache(lease);
    }
}

internal sealed class RenderTargetLease : IDisposable
{
    internal RenderTargetLease(RenderTargetLeaseSession session, PooledRenderTargetLease pooledLease)
    {
        Session = session;
        PooledLease = pooledLease;
    }

    public RenderTarget Target => PooledLease.Target;

    public bool IsReleased { get; internal set; }

    public bool WasReused => PooledLease.WasReused;

    internal RenderTargetLeaseSession Session { get; }

    internal PooledRenderTargetLease PooledLease { get; }

    public void Dispose()
    {
        Session.Release(this);
    }

    public RenderTarget TransferToAcceptedCache()
        => Session.TransferToAcceptedCache(this);
}

internal readonly record struct RenderTargetCacheContextIdentity(
    object BackendContextIdentity,
    long Generation);
