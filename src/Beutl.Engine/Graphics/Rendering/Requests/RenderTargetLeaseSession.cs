using System.Diagnostics.CodeAnalysis;

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
    private readonly long _maximumLiveBytes;
    private long _liveBytes;
    private List<RenderTargetLease>? _leases;
    private List<Exception>? _cleanupFailures;

    internal RenderTargetLeaseSession(
        RenderTargetPool pool,
        RenderIntent intent,
        object contextIdentity,
        long contextGeneration,
        nint? expectedContextHandle,
        RenderTarget? externalTarget,
        long maximumLiveBytes)
    {
        _pool = pool;
        _maximumLiveBytes = maximumLiveBytes;
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

    internal RenderCacheDeviceContextIdentity ProgramCacheDeviceContextIdentity => _pool.ResolveProgramCacheContext(this);

    internal long LiveBytes => Volatile.Read(ref _liveBytes);

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
    /// <param name="allowPreviewDrop">
    /// Whether a transient allocator refusal may drop the contribution. A budget refusal always degrades Preview.
    /// </param>
    public RenderTargetLease? TryAcquire(PixelSize deviceSize, bool clearContents = true, bool allowPreviewDrop = true)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_pool.TryAcquire(this, deviceSize, out RenderTargetLease? lease, out RenderTargetAllocationException? failure, clearContents))
            return lease;

        ReportAllocationFailure(failure, allowPreviewDrop);
        return null;
    }

    /// <summary>Allocates a fresh target counted until the last reference to its backing surface is released.</summary>
    internal RenderTarget? TryCreateUnpooled(PixelSize deviceSize)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (deviceSize.Width <= 0 || deviceSize.Height <= 0)
            return null;

        if (!TryReserveLiveBytes(deviceSize, out long byteSize, out RenderTargetAllocationException? failure))
        {
            ReportAllocationFailure(failure, allowPreviewDrop: true);
            return null;
        }

        RenderTarget? target = null;
        try
        {
            target = RenderTarget.Create(deviceSize.Width, deviceSize.Height);
            target?.TrackLiveBytes(this, byteSize);
            return target;
        }
        finally
        {
            if (target is null)
                ReleaseLiveBytes(byteSize);
        }
    }

    internal bool TryReserveLiveBytes(
        PixelSize deviceSize,
        out long byteSize,
        [NotNullWhen(false)] out RenderTargetAllocationException? failure)
    {
        const int BytesPerPixel = 8; // RGBA16F
        long pixelCount = (long)deviceSize.Width * deviceSize.Height;
        long liveBytes = LiveBytes;
        // Compare before multiplying to also handle sizes whose byte count would overflow.
        if (pixelCount > (_maximumLiveBytes - liveBytes) / BytesPerPixel)
        {
            byteSize = 0;
            failure = new RenderTargetAllocationException(deviceSize, liveBytes, _maximumLiveBytes);
            return false;
        }

        byteSize = pixelCount * BytesPerPixel;
        // Allocations within a request are synchronous. The last shallow reference can release on another thread.
        _pool.ObserveLiveBytes(Interlocked.Add(ref _liveBytes, byteSize));
        failure = null;
        return true;
    }

    internal void ReleaseLiveBytes(long byteSize) => Interlocked.Add(ref _liveBytes, -byteSize);

    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        try
        {
            for (int index = (_leases?.Count ?? 0) - 1; index >= 0; index--)
            {
                RenderTargetLease lease = _leases![index];
                if (lease.State == RenderTargetLeaseState.Leased)
                    _pool.Release(lease);
            }
        }
        finally
        {
            _leases = null;
            ExternalTarget = null;
            ExternalSurface = null;
            _pool.EndSession(this);
        }
    }

    public void ThrowIfCleanupFailed()
        => Requests.CleanupFailures.ThrowIfAny(
            _cleanupFailures,
            "One or more render targets failed to discharge.");

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

        _pool.Release(lease);
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

        _pool.DeferRelease(lease);
        var deferredRelease = new DeferredRenderTargetLeaseRelease(lease);
        if (!GpuResourceReclaimQueue.TryDefer(deferredRelease, lease.Slot.ByteSize))
        {
            try
            {
                if (lease.Target.Value.Context is GRContext context)
                    context.Flush(true, true);
                else
                    _pool.RetireCurrentContext();
            }
            catch (Exception ex)
            {
                RecordCleanupFailure(ex);
                try { _pool.RetireCurrentContext(); }
                catch (Exception retirementFailure) { RecordCleanupFailure(retirementFailure); }
            }
            deferredRelease.Dispose();
        }
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

    private void ReportAllocationFailure(RenderTargetAllocationException failure, bool allowPreviewDrop)
    {
        if (Intent == RenderIntent.Delivery || (!allowPreviewDrop && !failure.IsBudgetRefusal))
            throw failure;

        if (failure.IsBudgetRefusal)
            MarkContentDropped();

        s_logger.LogWarning("Intermediate render-target allocation failed in preview: {Reason}", failure.Message);
    }

    private void VerifyLeaseOwner(RenderTargetLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!ReferenceEquals(lease.Session, this))
            throw new InvalidOperationException("The render-target lease belongs to a different allocation session.");
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
                try { s_logger.LogError(ex, "Deferred render-target release failed."); }
                catch { }
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
