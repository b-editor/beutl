using System.Runtime.ExceptionServices;

using Beutl.Logging;
using Beutl.Media;

using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Rendering;

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

    public RenderTargetLeaseRegistry(IRenderTargetFactory? factory)
    {
        _pool = new RenderTargetPool(factory);
    }

    public RenderTargetPoolStatistics Statistics => _pool.Statistics;

    public RenderTargetLeaseSession BeginSession(
        RenderIntent intent,
        RenderAllocationBudget allocationBudget,
        RenderTarget? externalTarget = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(allocationBudget);
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
            allocationBudget,
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
           ?? throw RenderTargetPool.CreateAllocationFailure(deviceSize);

    /// <summary>
    /// Leases an intermediate target, returning <see langword="null"/> when a
    /// <see cref="RenderIntent.Preview"/> session may drop the caller's contribution instead.
    /// </summary>
    /// <remarks>A <see cref="RenderIntent.Delivery"/> session never degrades: it throws.</remarks>
    internal RenderTargetLease? TryAcquire(RenderTargetLeaseSession session, PixelSize deviceSize)
    {
        VerifyActive(session);
        if (!session.TryReserve(deviceSize, out long byteSize))
        {
            s_logger.LogWarning(
                "Intermediate render-target budget was exceeded ({Width}x{Height} px); preview drops this target, delivery render fails fast.",
                deviceSize.Width,
                deviceSize.Height);
            if (session.Intent == RenderIntent.Delivery)
                throw CreateBudgetFailure(deviceSize, session.AllocationBudget);
            return null;
        }

        PooledRenderTargetLease? pooled;
        try
        {
            if (session.Request.TryAcquire(deviceSize, out pooled))
            {
                var lease = new RenderTargetLease(session, pooled, byteSize);
                session.Register(lease);
                return lease;
            }
        }
        catch
        {
            session.CancelReservation(byteSize);
            throw;
        }

        session.CancelReservation(byteSize);
        if (pooled is null)
        {
            s_logger.LogWarning(
                "Intermediate render-target allocation failed ({Width}x{Height} px); preview drops this target, delivery render fails fast.",
                deviceSize.Width,
                deviceSize.Height);
            if (session.Intent == RenderIntent.Delivery)
                throw RenderTargetPool.CreateAllocationFailure(deviceSize);
            return null;
        }

        throw new InvalidOperationException("The render-target pool returned an invalid acquisition result.");
    }

    internal void Release(RenderTargetLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (lease.IsReleased)
            return;

        lease.IsReleased = true;
        lease.Session.Discharge(lease.ByteSize);
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
        lease.Session.Discharge(lease.ByteSize);
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

    private static InvalidOperationException CreateBudgetFailure(
        PixelSize deviceSize,
        RenderAllocationBudget budget)
        => new(
            $"The {deviceSize.Width}x{deviceSize.Height} RGBA16F render target exceeds the request-family "
            + $"allocation budget ({budget.MaximumLiveBytes} bytes, {budget.MaximumLiveTargets} targets).");
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
        RenderAllocationBudget allocationBudget,
        RenderTarget? externalTarget)
    {
        _registry = registry;
        Request = request;
        Intent = intent;
        AllocationBudget = allocationBudget;
        ExternalTarget = externalTarget;
    }

    public RenderIntent Intent { get; }

    internal RenderAllocationBudget AllocationBudget { get; }

    public bool IsDisposed { get; private set; }

    internal RenderTargetPoolRequest Request { get; }

    internal RenderTarget? ExternalTarget { get; }

    internal long LiveBytes { get; private set; }

    internal int LiveTargets { get; private set; }

    internal IReadOnlyList<Exception> CleanupFailures
        => _cleanupFailures.Concat(Request.CleanupFailures).ToArray();

    internal RenderTargetPoolStatistics PoolStatistics => _registry.Statistics;

    internal RenderCacheDeviceContextIdentity CacheDeviceContextIdentity
        => new(
            _registry,
            new RenderTargetCacheContextIdentity(
                Request.ContextIdentity,
                Request.ContextGeneration));

    internal RenderTargetCleanupFailureCheckpoint CaptureCleanupFailureCheckpoint()
        => new(this, _cleanupFailures.Count, Request.CleanupFailures.Count);

    internal IReadOnlyList<Exception> GetCleanupFailuresSince(
        RenderTargetCleanupFailureCheckpoint checkpoint)
    {
        if (!ReferenceEquals(checkpoint.Session, this)
            || checkpoint.SessionFailureCount < 0
            || checkpoint.SessionFailureCount > _cleanupFailures.Count
            || checkpoint.RequestFailureCount < 0
            || checkpoint.RequestFailureCount > Request.CleanupFailures.Count)
        {
            throw new ArgumentException(
                "The cleanup-failure checkpoint does not belong to this session.",
                nameof(checkpoint));
        }

        return
        [
            .. _cleanupFailures.Skip(checkpoint.SessionFailureCount),
            .. Request.CleanupFailures.Skip(checkpoint.RequestFailureCount),
        ];
    }

    public RenderTargetLease Acquire(PixelSize deviceSize)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return _registry.Acquire(this, deviceSize);
    }

    public RenderTargetLease? TryAcquire(PixelSize deviceSize)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return _registry.TryAcquire(this, deviceSize);
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        try
        {
            for (int index = _leases.Count - 1; index >= 0; index--)
                _registry.Release(_leases[index]);
            Request.Dispose();
        }
        finally
        {
            _registry.EndSession(this);
        }
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

    internal bool TryReserve(PixelSize deviceSize, out long byteSize)
    {
        byteSize = GetValidatedByteSize(deviceSize);

        if (byteSize > AllocationBudget.MaximumLiveBytes - LiveBytes
            || LiveTargets >= AllocationBudget.MaximumLiveTargets)
        {
            return false;
        }

        LiveBytes += byteSize;
        LiveTargets++;
        return true;
    }

    internal void ValidatePlannedAllocations(
        IReadOnlyList<PlannedRenderTargetLifetime> lifetimes)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(lifetimes);
        if (lifetimes.Count == 0)
            return;

        var starts = new Dictionary<int, List<long>>();
        var ends = new Dictionary<int, List<long>>();
        foreach (PlannedRenderTargetLifetime lifetime in lifetimes)
        {
            if (lifetime.AcquisitionPosition < 0
                || lifetime.LastUsePosition < lifetime.AcquisitionPosition)
            {
                throw new ArgumentException(
                    "A planned render-target lifetime must have a non-negative ordered interval.",
                    nameof(lifetimes));
            }

            long byteSize = GetValidatedByteSize(lifetime.DeviceSize);
            AddEvent(starts, lifetime.AcquisitionPosition, byteSize);
            AddEvent(ends, lifetime.LastUsePosition, byteSize);
        }

        long plannedBytes = LiveBytes;
        int plannedTargets = LiveTargets;
        int[] positions = [.. starts.Keys.Concat(ends.Keys).Distinct().Order()];
        foreach (int position in positions)
        {
            if (starts.TryGetValue(position, out List<long>? acquired))
            {
                foreach (long byteSize in acquired)
                {
                    if (byteSize > AllocationBudget.MaximumLiveBytes - plannedBytes
                        || plannedTargets >= AllocationBudget.MaximumLiveTargets)
                    {
                        throw new InvalidOperationException(
                            $"The planned render-target lifetime schedule exceeds the request-family "
                            + $"allocation budget ({AllocationBudget.MaximumLiveBytes} bytes, "
                            + $"{AllocationBudget.MaximumLiveTargets} targets).");
                    }

                    plannedBytes += byteSize;
                    plannedTargets++;
                }
            }

            // A consumer allocates its output before releasing its inputs, so intervals ending and
            // starting at the same schedule position overlap for the peak calculation.
            if (ends.TryGetValue(position, out List<long>? released))
            {
                foreach (long byteSize in released)
                {
                    plannedBytes -= byteSize;
                    plannedTargets--;
                }
            }
        }

        if (plannedBytes != LiveBytes || plannedTargets != LiveTargets)
            throw new InvalidOperationException("The planned render-target lifetime schedule is unbalanced.");
    }

    internal long GetValidatedByteSize(PixelSize deviceSize)
    {
        if (deviceSize.Width <= 0 || deviceSize.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(deviceSize));

        long byteSize;
        try
        {
            byteSize = checked((long)deviceSize.Width * deviceSize.Height * 8L);
        }
        catch (OverflowException ex)
        {
            throw new InvalidOperationException(
                "The RGBA16F render-target byte footprint overflowed 64-bit arithmetic.",
                ex);
        }

        int maximumDimension = Request.GetMaximumDimension(deviceSize);

        if (deviceSize.Width > maximumDimension || deviceSize.Height > maximumDimension)
        {
            throw new InvalidOperationException(
                $"The render target {deviceSize.Width}x{deviceSize.Height} exceeds the active "
                + $"{maximumDimension}-pixel dimension limit.");
        }

        return byteSize;
    }

    private static void AddEvent(
        IDictionary<int, List<long>> events,
        int position,
        long byteSize)
    {
        if (!events.TryGetValue(position, out List<long>? values))
        {
            values = [];
            events.Add(position, values);
        }

        values.Add(byteSize);
    }

    internal void CancelReservation(long byteSize) => Discharge(byteSize);

    internal void Discharge(long byteSize)
    {
        if (byteSize <= 0 || LiveTargets <= 0 || LiveBytes < byteSize)
            throw new InvalidOperationException("The render-target allocation ledger is corrupted.");

        LiveBytes -= byteSize;
        LiveTargets--;
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

internal readonly record struct RenderTargetCleanupFailureCheckpoint(
    RenderTargetLeaseSession Session,
    int SessionFailureCount,
    int RequestFailureCount);

internal readonly record struct PlannedRenderTargetLifetime(
    PixelSize DeviceSize,
    int AcquisitionPosition,
    int LastUsePosition);

internal sealed class RenderTargetLease : IDisposable
{
    internal RenderTargetLease(
        RenderTargetLeaseSession session,
        PooledRenderTargetLease pooledLease,
        long byteSize)
    {
        Session = session;
        PooledLease = pooledLease;
        ByteSize = byteSize;
    }

    public RenderTarget Target => PooledLease.Target;

    public bool IsReleased { get; internal set; }

    public bool WasReused => PooledLease.WasReused;

    internal RenderTargetLeaseSession Session { get; }

    internal PooledRenderTargetLease PooledLease { get; }

    internal long ByteSize { get; }

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
