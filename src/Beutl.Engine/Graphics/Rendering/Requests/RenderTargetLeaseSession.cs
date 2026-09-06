using System.Runtime.ExceptionServices;
using Beutl.Graphics.Backend;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Rendering.Requests;

/// <summary>One render request's allocation policy, context binding, leases, and cleanup record.</summary>
internal sealed class RenderTargetLeaseSession : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<RenderTargetLeaseSession>();

    private readonly RenderTargetPool _pool;
    private readonly RenderCacheDeviceContextIdentity _cacheDeviceContextIdentity;
    private List<RenderTargetLease>? _leases;
    private List<Exception>? _cleanupFailures;

    internal RenderTargetLeaseSession(
        RenderTargetPool pool,
        RenderIntent intent,
        object contextIdentity,
        long contextGeneration,
        nint? expectedContextHandle,
        RenderTarget? externalTarget)
    {
        _pool = pool;
        Intent = intent;
        ContextIdentity = contextIdentity;
        ContextGeneration = contextGeneration;
        ExpectedContextHandle = expectedContextHandle;
        ExternalTarget = externalTarget;
        ExternalSurface = externalTarget?.RawValue;
        _cacheDeviceContextIdentity = new RenderCacheDeviceContextIdentity(
            pool,
            new RenderTargetCacheContextIdentity(contextIdentity, contextGeneration));
    }

    public RenderIntent Intent { get; }

    /// <summary>Whether allocation routes must use the caller-supplied target factory.</summary>
    public bool HasTargetFactory => _pool.HasTargetFactory;

    /// <summary>Whether Preview dropped requested content outside the executor's own lease path.</summary>
    /// <remarks>A degraded frame cannot publish retained output or backdrop captures.</remarks>
    internal bool ContentDropObserved { get; private set; }

    public bool IsDisposed { get; private set; }

    public IReadOnlyList<Exception> CleanupFailures => _cleanupFailures ?? [];

    internal object ContextIdentity { get; }

    internal long ContextGeneration { get; }

    internal nint? ExpectedContextHandle { get; }

    internal RenderTarget? ExternalTarget { get; private set; }

    internal SKSurface? ExternalSurface { get; private set; }

    internal RenderCacheDeviceContextIdentity CacheDeviceContextIdentity => _cacheDeviceContextIdentity;

    internal void MarkContentDropped() => ContentDropObserved = true;

    internal RenderTargetCleanupFailureCheckpoint CaptureCleanupFailureCheckpoint()
        => new(this, _cleanupFailures?.Count ?? 0);

    internal IReadOnlyList<Exception> GetCleanupFailuresSince(
        RenderTargetCleanupFailureCheckpoint checkpoint)
    {
        int failureCount = _cleanupFailures?.Count ?? 0;
        if (!ReferenceEquals(checkpoint.Session, this)
            || checkpoint.FailureCount < 0
            || checkpoint.FailureCount > failureCount)
        {
            throw new ArgumentException(
                "The cleanup-failure checkpoint does not belong to this session.",
                nameof(checkpoint));
        }

        int count = failureCount - checkpoint.FailureCount;
        return count == 0 ? [] : _cleanupFailures!.GetRange(checkpoint.FailureCount, count);
    }

    public RenderTargetLease Acquire(PixelSize deviceSize)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return _pool.Acquire(this, deviceSize);
    }

    /// <summary>
    /// Leases an intermediate target, returning <see langword="null"/> when Preview may drop the caller's
    /// contribution. Delivery never degrades.
    /// </summary>
    /// <param name="clearContents">
    /// Whether the target must arrive transparent. Pass <see langword="false"/> only when every pixel is
    /// defined before any is read.
    /// </param>
    public RenderTargetLease? TryAcquire(PixelSize deviceSize, bool clearContents = true)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_pool.TryAcquire(this, deviceSize, out RenderTargetLease? lease, clearContents))
            return lease;

        s_logger.LogWarning(
            "Intermediate render-target allocation failed ({Width}x{Height} px); preview drops this target, delivery render fails fast.",
            deviceSize.Width,
            deviceSize.Height);
        if (Intent == RenderIntent.Delivery)
            throw CreateAllocationFailure(deviceSize);
        return null;
    }

    public bool ExceedsBufferBudget(PixelSize deviceSize)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return _pool.ExceedsBufferBudget(this, deviceSize, out _);
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        List<Exception>? releaseFailures = null;
        try
        {
            for (int index = (_leases?.Count ?? 0) - 1; index >= 0; index--)
            {
                RenderTargetLease lease = _leases![index];
                if (lease.State is not (RenderTargetLeaseState.Leased or RenderTargetLeaseState.ReleaseFailed))
                    continue;

                try
                {
                    _pool.Release(lease);
                }
                catch (Exception ex)
                {
                    Exception releaseFailure = lease.ReleaseFailure ?? ex;
                    if (lease.ReleaseFailure is null)
                    {
                        lease.ReleaseFailure = releaseFailure;
                        RecordCleanupFailure(releaseFailure);
                    }
                    AppendFailure(ref releaseFailures, releaseFailure);
                    try
                    {
                        _pool.EvictAfterReleaseFailure(lease);
                    }
                    catch (Exception cleanup)
                    {
                        AppendFailure(ref releaseFailures, cleanup);
                    }
                }
            }
        }
        finally
        {
            _leases = null;
            ExternalTarget = null;
            ExternalSurface = null;
            _pool.EndSession(this);
        }

        if (releaseFailures is { Count: 1 })
            ExceptionDispatchInfo.Capture(releaseFailures[0]).Throw();
        if (releaseFailures is { Count: > 1 })
        {
            throw new AggregateException(
                "One or more render-target leases failed to discharge.",
                releaseFailures);
        }
    }

    public void ThrowIfCleanupFailed()
    {
        if (_cleanupFailures is not { Count: > 0 } failures)
            return;
        if (failures.Count == 1)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();

        throw new AggregateException(
            "One or more render targets failed to discharge.",
            failures);
    }

    internal void Register(RenderTargetLease lease)
    {
        (_leases ??= []).Add(lease);
    }

    internal void RecordCleanupFailure(Exception exception)
    {
        (_cleanupFailures ??= []).Add(exception);
    }

    internal void Release(RenderTargetLease lease)
    {
        VerifyLeaseOwner(lease);
        if (lease.State != RenderTargetLeaseState.Leased)
            return;

        try
        {
            _pool.Release(lease);
        }
        catch (Exception ex)
        {
            lease.State = RenderTargetLeaseState.ReleaseFailed;
            lease.ReleaseFailure = ex;
            RecordCleanupFailure(ex);
        }
    }

    internal void ReleaseForBackendReuse(RenderTargetLease lease)
    {
        VerifyLeaseOwner(lease);
        if (lease.IsReleased)
            return;

        ITexture2D? texture = lease.Target.Texture;
        if (texture is not { RequiresSkiaFlushForBackendInterop: true })
        {
            Release(lease);
            return;
        }

        long approximateBytes = checked((long)lease.Target.Width * lease.Target.Height * 8);
        _pool.DeferRelease(lease);
        var deferredRelease = new DeferredRenderTargetLeaseRelease(lease);
        if (!GpuResourceReclaimQueue.TryDefer(deferredRelease, approximateBytes))
            deferredRelease.Dispose();
    }

    internal RenderTarget TransferToAcceptedCache(RenderTargetLease lease)
    {
        VerifyLeaseOwner(lease);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (lease.IsReleased)
            throw new InvalidOperationException("The render-target lease has already been discharged.");

        RenderTarget target = _pool.TransferToAcceptedCache(lease);
        return target;
    }

    private InvalidOperationException CreateAllocationFailure(PixelSize deviceSize)
        => _pool.ExceedsBufferBudget(this, deviceSize, out int maxDimension)
            ? RenderTargetPool.CreateAllocationFailure(deviceSize, maxDimension)
            : RenderTargetPool.CreateAllocationFailure(deviceSize);

    private void VerifyLeaseOwner(RenderTargetLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!ReferenceEquals(lease.Session, this))
            throw new InvalidOperationException("The render-target lease belongs to a different allocation session.");
    }

    private static void AppendFailure(ref List<Exception>? failures, Exception failure)
    {
        failures ??= [];
        if (failure is AggregateException aggregate)
        {
            foreach (Exception inner in aggregate.Flatten().InnerExceptions)
            {
                if (!failures.Contains(inner))
                    failures.Add(inner);
            }
        }
        else if (!failures.Contains(failure))
        {
            failures.Add(failure);
        }
    }

    private sealed class DeferredRenderTargetLeaseRelease(RenderTargetLease lease) : IDisposable
    {
        private RenderTargetLease? _lease = lease;

        public void Dispose()
        {
            RenderTargetLease? current = _lease;
            if (current is null)
                return;

            _lease = null;
            try
            {
                current.Session.Pool.CompleteDeferredRelease(current);
            }
            catch (Exception ex)
            {
                current.Session.RecordCleanupFailure(ex);
            }
        }
    }

    internal RenderTargetPool Pool => _pool;
}

internal readonly record struct RenderTargetCleanupFailureCheckpoint(
    RenderTargetLeaseSession Session,
    int FailureCount);

internal readonly record struct RenderTargetCacheContextIdentity(
    object BackendContextIdentity,
    long Generation);
