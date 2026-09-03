using System.Runtime.ExceptionServices;

using Beutl.Graphics.Backend;
using Beutl.Logging;
using Beutl.Media;

using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Rendering.Requests;

/// <summary>
/// Compatibility-facing adapter over the renderer-lifetime target pool. Request code keeps the
/// original lease vocabulary while released targets remain available for exact-size reuse until
/// the owning renderer is disposed.
/// </summary>
internal sealed class RenderTargetLeaseRegistry : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<RenderTargetLeaseRegistry>();

    private readonly RenderTargetPool _pool;
    private RenderTargetLeaseSession? _activeSession;
    private bool _disposed;

    /// <param name="factory">The caller's allocator, or <see langword="null"/> for the engine's own.</param>
    /// <param name="maxBufferDimension">
    /// The largest extent to allocate, or <see langword="null"/> to bound each allocation by whatever its
    /// own allocator answers to.
    /// </param>
    public RenderTargetLeaseRegistry(IRenderTargetFactory? factory, int? maxBufferDimension = null)
    {
        HasTargetFactory = factory is not null;
        _pool = new RenderTargetPool(
            factory,
            new RenderTargetPoolOptions { MaxBufferDimension = maxBufferDimension });
    }

    public RenderTargetPoolStatistics Statistics => _pool.Statistics;

    /// <summary>
    /// Whether the caller supplied an <see cref="IRenderTargetFactory"/>. Paths that allocate their own
    /// surfaces consult this: with a factory they must route through the session so its allocation policy and
    /// graphics context are honoured, and without one they keep their own allocation and failure reporting.
    /// </summary>
    public bool HasTargetFactory { get; }

    public RenderTargetLeaseSession BeginSession(
        RenderIntent intent,
        RenderTarget? externalTarget = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_activeSession is not null)
        {
            throw new InvalidOperationException(
                "Concurrent render-target allocation sessions on one renderer are unsupported.");
        }

        RenderTargetPoolRequest request = _pool.BeginRequest(externalTarget);
        var session = new RenderTargetLeaseSession(
            this,
            request,
            intent,
            externalTarget);
        _activeSession = session;
        return session;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        List<Exception> failures = [];
        try
        {
            _activeSession?.Dispose();
        }
        catch (Exception ex)
        {
            AppendFailures(failures, ex);
        }

        _activeSession = null;
        try
        {
            _pool.Dispose();
        }
        catch (Exception ex)
        {
            AppendFailures(failures, ex);
        }

        if (failures.Count == 1)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1)
        {
            throw new AggregateException(
                "One or more render-target registry resources failed to dispose.",
                failures);
        }
    }

    /// <summary>Evicts every unleased retained target and reports the released byte count.</summary>
    /// <remarks>Disposes backend resources, so it must run on the renderer's thread.</remarks>
    public long ReleaseRetainedTargets()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _pool.ReleaseRetainedTargets();
    }

    internal RenderTargetLease Acquire(RenderTargetLeaseSession session, PixelSize deviceSize)
        => TryAcquire(session, deviceSize)
           ?? throw CreateAllocationFailure(session, deviceSize);

    /// <summary>
    /// Whether no allocator this session can reach will ever attach <paramref name="deviceSize"/>.
    /// </summary>
    /// <remarks>
    /// Distinguishes the device's own limit from a momentary decline: a caller that must not degrade under
    /// allocation pressure still has nothing to wait for once this reports <see langword="true"/>.
    /// </remarks>
    internal bool ExceedsBufferBudget(RenderTargetLeaseSession session, PixelSize deviceSize)
    {
        VerifyActive(session);
        return _pool.ExceedsBufferBudget(session.Request, deviceSize, out _);
    }

    /// <summary>
    /// Leases an intermediate target, returning <see langword="null"/> when a
    /// <see cref="RenderIntent.Preview"/> session may drop the caller's contribution instead.
    /// </summary>
    /// <remarks>A <see cref="RenderIntent.Delivery"/> session never degrades: it throws.</remarks>
    internal RenderTargetLease? TryAcquire(
        RenderTargetLeaseSession session,
        PixelSize deviceSize,
        bool clearContents = true)
    {
        VerifyActive(session);
        if (!session.Request.TryAcquire(deviceSize, out PooledRenderTargetLease? pooled, clearContents))
        {
            s_logger.LogWarning(
                "Intermediate render-target allocation failed ({Width}x{Height} px); preview drops this target, delivery render fails fast.",
                deviceSize.Width,
                deviceSize.Height);
            if (session.Intent == RenderIntent.Delivery)
                throw CreateAllocationFailure(session, deviceSize);
            return null;
        }

        var lease = new RenderTargetLease(session, pooled);
        session.Register(lease);
        return lease;
    }

    /// <summary>
    /// Describes a refused allocation, naming the device limit when that is what refused it.
    /// </summary>
    private InvalidOperationException CreateAllocationFailure(
        RenderTargetLeaseSession session,
        PixelSize deviceSize)
        => _pool.ExceedsBufferBudget(session.Request, deviceSize, out int maxDimension)
            ? RenderTargetPool.CreateAllocationFailure(deviceSize, maxDimension)
            : RenderTargetPool.CreateAllocationFailure(deviceSize);

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

    internal void ReleaseForBackendReuse(RenderTargetLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (lease.IsReleased)
            return;

        ITexture2D? texture = lease.Target.Texture;
        if (texture is not { RequiresSkiaFlushForBackendInterop: true })
        {
            Release(lease);
            return;
        }

        long approximateBytes = checked((long)lease.Target.Width * lease.Target.Height * 8);
        lease.PooledLease.DeferRelease();
        lease.IsReleased = true;
        var deferredRelease = new DeferredRenderTargetLeaseRelease(lease);
        if (!GpuResourceReclaimQueue.TryDefer(deferredRelease, approximateBytes))
            deferredRelease.Dispose();
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

    private static void AppendFailures(List<Exception> failures, Exception failure)
    {
        if (failure is AggregateException aggregate)
            failures.AddRange(aggregate.Flatten().InnerExceptions);
        else
            failures.Add(failure);
    }
}

internal sealed class DeferredRenderTargetLeaseRelease : IDisposable
{
    private RenderTargetLease? _lease;

    public DeferredRenderTargetLeaseRelease(RenderTargetLease lease)
    {
        _lease = lease ?? throw new ArgumentNullException(nameof(lease));
    }

    public void Dispose()
    {
        RenderTargetLease? lease = _lease;
        if (lease is null)
            return;

        _lease = null;
        try
        {
            lease.PooledLease.CompleteDeferredRelease();
        }
        catch (Exception ex)
        {
            lease.Session.RecordCleanupFailure(ex);
        }
    }
}

internal readonly record struct RenderTargetCacheContextIdentity(
    object BackendContextIdentity,
    long Generation);
