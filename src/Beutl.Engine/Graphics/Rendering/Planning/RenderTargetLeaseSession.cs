using System.Runtime.ExceptionServices;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

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

    /// <inheritdoc cref="RenderTargetLeaseRegistry.HasTargetFactory"/>
    public bool HasTargetFactory => _registry.HasTargetFactory;

    /// <summary>
    /// Whether a path that allocates its own surfaces dropped content it was asked to draw rather than
    /// failing the render.
    /// </summary>
    /// <remarks>
    /// Tile-brush intermediates, custom-effect targets, and effect flush buffers degrade to transparent
    /// under <see cref="RenderIntent.Preview"/> instead of throwing, and the executor's own drop
    /// observation cannot see them: they never take a lease. Folding this in keeps a frame that is missing
    /// pixels out of anything that outlives it — a render cache or a captured backdrop.
    /// </remarks>
    internal bool ContentDropObserved { get; private set; }

    /// <summary>Records that content this session backs was dropped for want of a target.</summary>
    internal void MarkContentDropped() => ContentDropObserved = true;

    public bool IsDisposed { get; private set; }

    internal RenderTargetPoolRequest Request { get; }

    internal RenderTarget? ExternalTarget { get; }

    internal IReadOnlyList<Exception> CleanupFailures
        => _cleanupFailures.Count == 0 && Request.CleanupFailures.Count == 0
            ? []
            : [.. _cleanupFailures, .. Request.CleanupFailures];

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

    /// <param name="clearContents">
    /// Whether the lease must arrive transparent. Pass <see langword="false"/> only when every pixel is
    /// defined before any is read.
    /// </param>
    public RenderTargetLease? TryAcquire(PixelSize deviceSize, bool clearContents = true)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return _registry.TryAcquire(this, deviceSize, clearContents);
    }

    /// <inheritdoc cref="RenderTargetLeaseRegistry.ExceedsBufferBudget"/>
    public bool ExceedsBufferBudget(PixelSize deviceSize)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return _registry.ExceedsBufferBudget(this, deviceSize);
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
        IReadOnlyList<Exception> failures = CleanupFailures;
        if (failures.Count == 0)
            return;
        if (failures.Count == 1)
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

    internal void ReleaseForBackendReuse(RenderTargetLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!ReferenceEquals(lease.Session, this))
            throw new InvalidOperationException("The render-target lease belongs to a different allocation session.");
        _registry.ReleaseForBackendReuse(lease);
    }

    internal RenderTarget TransferToAcceptedCache(RenderTargetLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!ReferenceEquals(lease.Session, this))
            throw new InvalidOperationException("The render-target lease belongs to a different allocation session.");
        return _registry.TransferToAcceptedCache(lease);
    }
}
