using System.Runtime.ExceptionServices;

using Beutl.Media;

using SkiaSharp;

namespace Beutl.Graphics.Rendering;

internal sealed class RenderTargetPoolOptions
{
    public const long DefaultMaximumRetainedBytes = 256L * 1024 * 1024;

    public long MaximumRetainedBytes { get; init; } = DefaultMaximumRetainedBytes;

    public int MaximumIdleRequests { get; init; } = 120;
}

internal readonly record struct RenderTargetPoolStatistics(
    long Creates,
    long Reuses,
    long Misses,
    long Evictions,
    int OwnedTargets,
    int AvailableTargets,
    int LeasedTargets,
    long OwnedBytes,
    long RetainedBytes,
    int PeakLiveTargets);

internal enum PooledRenderTargetLeaseState : byte
{
    Leased,
    Available,
    Evicted,
    CacheTransferred,
}

/// <summary>
/// Renderer-lifetime owner for exact-size, linear-premultiplied RGBA16F intermediate targets.
/// </summary>
internal sealed class RenderTargetPool : IDisposable
{
    private static readonly object s_cpuContextIdentity = new();
    private static readonly object s_implicitContextIdentity = new();

    private readonly IRenderTargetFactory? _factory;
    private readonly RenderTargetPoolOptions _options;
    private readonly Dictionary<PixelSize, LinkedList<TargetSlot>> _availableBuckets = [];
    private readonly LinkedList<TargetSlot> _availableLru = [];
    private readonly HashSet<TargetSlot> _ownedSlots = [];
    private readonly HashSet<RenderTarget> _knownTargets = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<SKSurface> _knownSurfaces = new(ReferenceEqualityComparer.Instance);
    private RenderTargetPoolRequest? _activeRequest;
    private object? _contextIdentity;
    private nint _contextHandle;
    private bool _hasContext;
    private long _requestEpoch;
    private long _nextLeaseGeneration;
    private long _contextGeneration;
    private long _ownedBytes;
    private long _retainedBytes;
    private long _creates;
    private long _reuses;
    private long _misses;
    private long _evictions;
    private int _leasedTargets;
    private int _peakLiveTargets;
    private bool _disposed;

    public RenderTargetPool(
        IRenderTargetFactory? factory,
        RenderTargetPoolOptions? options = null)
    {
        options ??= new RenderTargetPoolOptions();
        if (options.MaximumRetainedBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "The retained-byte limit cannot be negative.");
        if (options.MaximumIdleRequests < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "The idle-request limit cannot be negative.");

        _factory = factory;
        _options = new RenderTargetPoolOptions
        {
            MaximumRetainedBytes = options.MaximumRetainedBytes,
            MaximumIdleRequests = options.MaximumIdleRequests,
        };
    }

    public RenderTargetPoolStatistics Statistics => new(
        _creates,
        _reuses,
        _misses,
        _evictions,
        _ownedSlots.Count,
        _availableLru.Count,
        _leasedTargets,
        _ownedBytes,
        _retainedBytes,
        _peakLiveTargets);

    public RenderTargetPoolRequest BeginRequest(RenderTarget? externalTarget = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (externalTarget is not null)
        {
            externalTarget.VerifyAccess();
            SKSurface surface = externalTarget.Value;
            GRRecordingContext? context = surface.Context;
            return BeginRequestCore(
                context ?? s_cpuContextIdentity,
                context?.Handle ?? 0,
                externalTarget);
        }

        object contextIdentity = _hasContext ? _contextIdentity! : s_implicitContextIdentity;
        return BeginRequestCore(contextIdentity, expectedContextHandle: null, externalTarget: null);
    }

    public RenderTargetPoolRequest BeginRequestForContext(
        object contextIdentity,
        nint expectedContextHandle,
        RenderTarget? externalTarget = null)
    {
        ArgumentNullException.ThrowIfNull(contextIdentity);
        ObjectDisposedException.ThrowIf(_disposed, this);
        externalTarget?.VerifyAccess();
        return BeginRequestCore(contextIdentity, expectedContextHandle, externalTarget);
    }

    public void ResetContext()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_activeRequest is not null)
            throw new InvalidOperationException("The render-target pool context cannot change during an active request.");

        List<Exception> failures = [];
        EvictAllAvailable(failures);
        _contextIdentity = null;
        _contextHandle = 0;
        _hasContext = false;
        ThrowCleanupFailures(failures);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        List<Exception> failures = [];
        RenderTargetPoolRequest? activeRequest = _activeRequest;
        activeRequest?.Dispose();
        failures.AddRange(activeRequest?.CleanupFailures ?? []);
        _activeRequest = null;

        foreach (TargetSlot slot in _ownedSlots.ToArray())
            Evict(slot, request: null, failures);

        _availableBuckets.Clear();
        _availableLru.Clear();
        _knownTargets.Clear();
        _knownSurfaces.Clear();
        ThrowCleanupFailures(failures);
    }

    internal PooledRenderTargetLease Acquire(
        RenderTargetPoolRequest request,
        PixelSize deviceSize)
    {
        VerifyActive(request);
        if (deviceSize.Width <= 0 || deviceSize.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deviceSize),
                deviceSize,
                "A pooled render target requires a positive device size.");
        }

        if (TryTakeAvailable(deviceSize, out TargetSlot? slot))
        {
            TargetSlot reusable = slot!;
            try
            {
                ValidateReusableSlot(reusable, request);
            }
            catch (Exception ex)
            {
                Evict(reusable, request, failures: null);
                ExceptionDispatchInfo.Capture(ex).Throw();
                throw;
            }

            _reuses++;
            return Lease(request, reusable, wasReused: true);
        }

        _misses++;
        RenderTarget? target = _factory?.Create(deviceSize)
            ?? (_factory is null ? CreateDefaultTarget(deviceSize, request) : null);
        if (target is null)
        {
            throw new InvalidOperationException(
                $"The render-target factory could not allocate {deviceSize.Width}x{deviceSize.Height} pixels.");
        }

        bool accepted = false;
        bool targetIsBorrowedOrAlreadyOwned = ReferenceEquals(target, request.ExternalTarget)
            || _knownTargets.Contains(target);
        try
        {
            SKSurface surface = ValidateFactoryTarget(target, deviceSize, request);
            long byteSize = GetByteSize(deviceSize);
            slot = new TargetSlot(target, surface, deviceSize, byteSize);
            _ownedSlots.Add(slot);
            _knownTargets.Add(target);
            _knownSurfaces.Add(surface);
            _ownedBytes = checked(_ownedBytes + byteSize);
            _creates++;
            accepted = true;
            return Lease(request, slot, wasReused: false);
        }
        catch (Exception primary)
        {
            if (!accepted && !targetIsBorrowedOrAlreadyOwned)
            {
                try
                {
                    target.Dispose();
                }
                catch (Exception cleanup)
                {
                    request.RecordCleanupFailure(cleanup);
                }
            }

            ExceptionDispatchInfo.Capture(primary).Throw();
            throw;
        }
    }

    internal void Release(PooledRenderTargetLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        VerifyLease(lease);

        TargetSlot slot = lease.Slot;
        lease.State = PooledRenderTargetLeaseState.Available;
        slot.ActiveLease = null;
        slot.LastAvailableLease = lease;
        slot.LastUsedEpoch = _requestEpoch;
        _leasedTargets--;

        if (_disposed || !IsCurrentContext(lease.Request) || slot.Target.IsDisposed)
        {
            lease.State = PooledRenderTargetLeaseState.Evicted;
            Evict(slot, lease.Request, failures: null);
            return;
        }

        AddAvailable(slot);
        TrimToByteBudget(lease.Request);
        if (!_ownedSlots.Contains(slot))
            lease.State = PooledRenderTargetLeaseState.Evicted;
    }

    internal RenderTarget TransferToAcceptedCache(PooledRenderTargetLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        VerifyLease(lease);

        TargetSlot slot = lease.Slot;
        slot.ActiveLease = null;
        slot.LastAvailableLease = null;
        lease.State = PooledRenderTargetLeaseState.CacheTransferred;
        _leasedTargets--;
        RemoveOwnedSlot(slot);
        return slot.Target;
    }

    internal void EndRequest(RenderTargetPoolRequest request)
    {
        if (ReferenceEquals(_activeRequest, request))
            _activeRequest = null;
    }

    private RenderTargetPoolRequest BeginRequestCore(
        object contextIdentity,
        nint? expectedContextHandle,
        RenderTarget? externalTarget)
    {
        if (_activeRequest is not null)
        {
            throw new InvalidOperationException(
                "Concurrent render-target pool requests on one renderer are unsupported.");
        }

        List<Exception> failures = [];
        if (_hasContext && !ReferenceEquals(_contextIdentity, contextIdentity))
            EvictAllAvailable(failures);

        if (!_hasContext || !ReferenceEquals(_contextIdentity, contextIdentity))
        {
            _contextIdentity = contextIdentity;
            _contextHandle = expectedContextHandle ?? 0;
            _hasContext = expectedContextHandle.HasValue;
            _contextGeneration = NextGeneration(_contextGeneration);
        }
        else if (expectedContextHandle.HasValue && _contextHandle != expectedContextHandle.Value)
        {
            EvictAllAvailable(failures);
            _contextHandle = expectedContextHandle.Value;
            _hasContext = true;
            _contextGeneration = NextGeneration(_contextGeneration);
        }

        ThrowCleanupFailures(failures);
        _requestEpoch++;
        var request = new RenderTargetPoolRequest(
            this,
            contextIdentity,
            _contextGeneration,
            expectedContextHandle,
            externalTarget);
        _activeRequest = request;
        TrimIdle(request);
        return request;
    }

    private static long NextGeneration(long current)
        => current == long.MaxValue ? 1 : current + 1;

    private PooledRenderTargetLease Lease(
        RenderTargetPoolRequest request,
        TargetSlot slot,
        bool wasReused)
    {
        long generation = ++_nextLeaseGeneration;
        if (generation <= 0)
        {
            _nextLeaseGeneration = 1;
            generation = 1;
        }

        var lease = new PooledRenderTargetLease(this, request, slot, generation, wasReused);
        slot.Generation = generation;
        slot.LastAvailableLease = null;
        slot.ActiveLease = lease;
        _leasedTargets++;
        _peakLiveTargets = Math.Max(_peakLiveTargets, _leasedTargets);
        request.Register(lease);
        return lease;
    }

    private bool TryTakeAvailable(PixelSize size, out TargetSlot? slot)
    {
        if (_availableBuckets.TryGetValue(size, out LinkedList<TargetSlot>? bucket)
            && bucket.Last is { } node)
        {
            slot = node.Value;
            RemoveAvailable(slot);
            return true;
        }

        slot = null;
        return false;
    }

    private void AddAvailable(TargetSlot slot)
    {
        if (!_availableBuckets.TryGetValue(slot.Size, out LinkedList<TargetSlot>? bucket))
        {
            bucket = [];
            _availableBuckets.Add(slot.Size, bucket);
        }

        slot.BucketNode = bucket.AddLast(slot);
        slot.LruNode = _availableLru.AddLast(slot);
        _retainedBytes = checked(_retainedBytes + slot.ByteSize);
    }

    private void RemoveAvailable(TargetSlot slot)
    {
        if (slot.BucketNode is { } bucketNode
            && _availableBuckets.TryGetValue(slot.Size, out LinkedList<TargetSlot>? bucket))
        {
            bucket.Remove(bucketNode);
            if (bucket.Count == 0)
                _availableBuckets.Remove(slot.Size);
        }

        if (slot.LruNode is { } lruNode)
            _availableLru.Remove(lruNode);

        if (slot.BucketNode is not null || slot.LruNode is not null)
            _retainedBytes -= slot.ByteSize;
        slot.BucketNode = null;
        slot.LruNode = null;
    }

    private void TrimIdle(RenderTargetPoolRequest request)
    {
        while (_availableLru.First is { } node
               && _requestEpoch - node.Value.LastUsedEpoch > _options.MaximumIdleRequests)
        {
            Evict(node.Value, request, failures: null);
        }
    }

    private void TrimToByteBudget(RenderTargetPoolRequest request)
    {
        while (_retainedBytes > _options.MaximumRetainedBytes
               && _availableLru.First is { } node)
        {
            Evict(node.Value, request, failures: null);
        }
    }

    private void EvictAllAvailable(List<Exception> failures)
    {
        while (_availableLru.First is { } node)
            Evict(node.Value, request: null, failures);
    }

    private void Evict(
        TargetSlot slot,
        RenderTargetPoolRequest? request,
        List<Exception>? failures)
    {
        if (!_ownedSlots.Contains(slot))
            return;

        PooledRenderTargetLease? liveLease = slot.ActiveLease;
        if (liveLease is not null)
        {
            liveLease.State = PooledRenderTargetLeaseState.Evicted;
            slot.ActiveLease = null;
            _leasedTargets--;
        }
        else if (slot.LastAvailableLease is { State: PooledRenderTargetLeaseState.Available } availableLease)
        {
            availableLease.State = PooledRenderTargetLeaseState.Evicted;
        }
        slot.LastAvailableLease = null;

        RemoveAvailable(slot);
        RemoveOwnedSlot(slot);
        _evictions++;
        try
        {
            slot.Target.Dispose();
        }
        catch (Exception ex)
        {
            if (request is not null)
                request.RecordCleanupFailure(ex);
            else
                failures?.Add(ex);
        }
    }

    private void RemoveOwnedSlot(TargetSlot slot)
    {
        if (!_ownedSlots.Remove(slot))
            return;

        RemoveAvailable(slot);
        _knownTargets.Remove(slot.Target);
        _knownSurfaces.Remove(slot.Surface);
        _ownedBytes -= slot.ByteSize;
    }

    private SKSurface ValidateFactoryTarget(
        RenderTarget target,
        PixelSize size,
        RenderTargetPoolRequest request)
    {
        if (ReferenceEquals(target, request.ExternalTarget))
        {
            throw new InvalidOperationException(
                "The render-target factory returned the borrowed destination as an owned allocation.");
        }
        if (_knownTargets.Contains(target))
        {
            throw new InvalidOperationException(
                "The render-target factory returned a target instance already owned by this pool.");
        }

        SKSurface surface = ValidateSurface(target, size);
        if (ReferenceEquals(surface, request.ExternalSurface) || _knownSurfaces.Contains(surface))
        {
            throw new InvalidOperationException(
                "The render-target factory returned a backing surface that is already in use.");
        }

        ValidateContext(surface, request);
        return surface;
    }

    private void ValidateReusableSlot(TargetSlot slot, RenderTargetPoolRequest request)
    {
        if (!_ownedSlots.Contains(slot)
            || slot.ActiveLease is not null
            || slot.Target.IsDisposed)
        {
            throw new InvalidOperationException("The pooled render target is no longer reusable.");
        }

        SKSurface surface = ValidateSurface(slot.Target, slot.Size);
        if (!ReferenceEquals(surface, slot.Surface))
            throw new InvalidOperationException("A pooled render target changed its backing surface.");
        ValidateContext(surface, request);
    }

    private static SKSurface ValidateSurface(RenderTarget target, PixelSize size)
    {
        if (target.IsDisposed || target.Width != size.Width || target.Height != size.Height)
        {
            throw new InvalidOperationException(
                "The render-target factory returned a disposed target or a target whose exact device size is wrong.");
        }

        target.VerifyAccess();
        SKSurface surface = target.Value;
        SKRectI deviceClip = surface.Canvas.DeviceClipBounds;
        if (deviceClip.Left != 0
            || deviceClip.Top != 0
            || deviceClip.Width != size.Width
            || deviceClip.Height != size.Height)
        {
            throw new InvalidOperationException(
                "The render-target surface has an incompatible device viewport.");
        }

        using SKImage? image = surface.Snapshot();
        using SKColorSpace expectedColorSpace = SKColorSpace.CreateSrgbLinear();
        using SKColorSpace? actualColorSpace = image?.ColorSpace;
        if (image is null
            || image.Width != size.Width
            || image.Height != size.Height
            || image.ColorType != SKColorType.RgbaF16
            || image.AlphaType != SKAlphaType.Premul
            || actualColorSpace is null
            || !SKColorSpace.Equal(actualColorSpace, expectedColorSpace))
        {
            throw new InvalidOperationException(
                "Pooled render targets must be linear-premultiplied RGBA16F surfaces.");
        }

        return surface;
    }

    private void ValidateContext(SKSurface surface, RenderTargetPoolRequest request)
    {
        nint actual = surface.Context?.Handle ?? 0;
        if (request.ExpectedContextHandle is { } expected && actual != expected)
        {
            throw new InvalidOperationException(
                "The render-target factory returned a target from an incompatible graphics context.");
        }

        if (!_hasContext)
        {
            _contextIdentity = request.ContextIdentity;
            _contextHandle = actual;
            _hasContext = true;
        }
        else if (!ReferenceEquals(_contextIdentity, request.ContextIdentity)
                 || _contextHandle != actual)
        {
            throw new InvalidOperationException(
                "The render-target factory returned targets from incompatible graphics contexts.");
        }
    }

    private void VerifyActive(RenderTargetPoolRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        if (!ReferenceEquals(_activeRequest, request) || request.IsDisposed)
            throw new InvalidOperationException("The render-target pool request is no longer active.");
    }

    private void VerifyLease(PooledRenderTargetLease lease)
    {
        if (!ReferenceEquals(lease.Pool, this))
            throw new InvalidOperationException("The render-target lease belongs to a different pool.");
        if (lease.State != PooledRenderTargetLeaseState.Leased)
        {
            throw new InvalidOperationException(
                $"The render-target lease has already been discharged as {lease.State}.");
        }

        TargetSlot slot = lease.Slot;
        if (!ReferenceEquals(slot.ActiveLease, lease) || slot.Generation != lease.Generation)
            throw new InvalidOperationException("The render-target lease generation is stale.");
    }

    private bool IsCurrentContext(RenderTargetPoolRequest request)
        => _hasContext && ReferenceEquals(_contextIdentity, request.ContextIdentity);

    private static long GetByteSize(PixelSize size)
    {
        try
        {
            return checked((long)size.Width * size.Height * 8);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(size),
                size,
                "The RGBA16F render-target byte size overflowed.");
        }
    }

    private static RenderTarget? CreateDefaultTarget(
        PixelSize deviceSize,
        RenderTargetPoolRequest request)
    {
        if (request.ExpectedContextHandle == 0)
        {
            SKSurface? surface = SKSurface.Create(new SKImageInfo(
                deviceSize.Width,
                deviceSize.Height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear()));
            return surface is null
                ? null
                : new CpuRenderTarget(surface, deviceSize);
        }

        return RenderTarget.Create(deviceSize.Width, deviceSize.Height);
    }

    private static void ThrowCleanupFailures(List<Exception> failures)
    {
        if (failures.Count == 0)
            return;
        if (failures.Count == 1)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        throw new AggregateException("One or more pooled render targets failed to dispose.", failures);
    }

    internal sealed class TargetSlot(
        RenderTarget target,
        SKSurface surface,
        PixelSize size,
        long byteSize)
    {
        public RenderTarget Target { get; } = target;

        public SKSurface Surface { get; } = surface;

        public PixelSize Size { get; } = size;

        public long ByteSize { get; } = byteSize;

        public long Generation { get; set; }

        public long LastUsedEpoch { get; set; }

        public PooledRenderTargetLease? ActiveLease { get; set; }

        public PooledRenderTargetLease? LastAvailableLease { get; set; }

        public LinkedListNode<TargetSlot>? BucketNode { get; set; }

        public LinkedListNode<TargetSlot>? LruNode { get; set; }
    }

    private sealed class CpuRenderTarget(SKSurface surface, PixelSize size)
        : RenderTarget(surface, size.Width, size.Height);
}

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
        ExternalSurface = externalTarget?.Value;
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

    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        for (int index = _leases.Count - 1; index >= 0; index--)
        {
            PooledRenderTargetLease lease = _leases[index];
            if (lease.State == PooledRenderTargetLeaseState.Leased)
                _pool.Release(lease);
        }

        _pool.EndRequest(this);
    }

    public void ThrowAfterCleanup(ExceptionDispatchInfo? primaryFailure)
    {
        primaryFailure?.Throw();
        if (_cleanupFailures.Count == 1)
            ExceptionDispatchInfo.Capture(_cleanupFailures[0]).Throw();
        if (_cleanupFailures.Count > 1)
        {
            throw new AggregateException(
                "One or more pooled render targets failed to discharge.",
                _cleanupFailures);
        }
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

internal sealed class PooledRenderTargetLease : IDisposable
{
    internal PooledRenderTargetLease(
        RenderTargetPool pool,
        RenderTargetPoolRequest request,
        RenderTargetPool.TargetSlot slot,
        long generation,
        bool wasReused)
    {
        Pool = pool;
        Request = request;
        Slot = slot;
        Generation = generation;
        WasReused = wasReused;
    }

    public RenderTarget Target => Slot.Target;

    public PixelSize DeviceSize => Slot.Size;

    public long Generation { get; }

    public bool WasReused { get; }

    public PooledRenderTargetLeaseState State { get; internal set; } = PooledRenderTargetLeaseState.Leased;

    internal RenderTargetPool Pool { get; }

    internal RenderTargetPoolRequest Request { get; }

    internal RenderTargetPool.TargetSlot Slot { get; }

    public RenderTarget TransferToAcceptedCache()
        => Pool.TransferToAcceptedCache(this);

    public void Dispose()
        => Pool.Release(this);
}
