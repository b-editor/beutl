using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

using Beutl.Graphics.Backend;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.Graphics.Rendering;

internal sealed class RenderTargetPoolOptions
{
    public const long DefaultMaximumRetainedBytes = 256L * 1024 * 1024;

    public long MaximumRetainedBytes { get; init; } = DefaultMaximumRetainedBytes;

    public int MaximumIdleRequests { get; init; } = 120;

    /// <summary>
    /// The largest extent this pool will allocate, or <see langword="null"/> to bound each allocation by
    /// whatever its own allocator answers to.
    /// </summary>
    /// <remarks>
    /// Naming one lets a test pin a limit below every device it runs on, so the refusal is observable without
    /// depending on the machine's GPU, and it binds a caller-supplied allocator too.
    /// </remarks>
    public int? MaxBufferDimension { get; init; }

    internal Action<RenderTargetPoolRegistrationStage>? AfterTargetRegistrationStep { get; init; }

    internal Action? BeforeLeaseRegistration { get; init; }
}

internal enum RenderTargetPoolRegistrationStage : byte
{
    OwnedSlot,
    KnownTarget,
    KnownSurface,
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
    Deferred,
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

    /// <summary>
    /// The identity a target-less request on the engine's own allocator uses, and the shared context it was
    /// minted for.
    /// </summary>
    /// <remarks>
    /// The two live in one object so a request can never take an identity minted for one context while another
    /// is live. <see cref="GraphicsContextFactory.Shutdown"/> is public, so the shared context is replaceable
    /// while the pool still holds the previous one's surfaces; minting a new identity for the new context is
    /// what makes <see cref="BeginRequestCore"/> evict them, rather than validating them against the handle the
    /// replaced context reported and then clearing and drawing through a device that is gone.
    /// </remarks>
    private sealed class ImplicitContextBinding(IGraphicsContext? context)
    {
        public IGraphicsContext? Context { get; } = context;
    }

    private readonly IRenderTargetFactory? _factory;
    private readonly RenderTargetPoolOptions _options;
    private readonly Dictionary<PixelSize, LinkedList<TargetSlot>> _availableBuckets = [];
    private readonly LinkedList<TargetSlot> _availableLru = [];
    private readonly HashSet<TargetSlot> _ownedSlots = [];
    private readonly HashSet<RenderTarget> _knownTargets = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<SKSurface> _knownSurfaces = new(ReferenceEqualityComparer.Instance);
    private RenderTargetPoolRequest? _activeRequest;
    private ImplicitContextBinding _implicitBinding = new(null);
    private object? _contextIdentity;
    private GRRecordingContext? _graphicsContext;
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
        if (options.MaxBufferDimension is <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "The maximum buffer dimension must be positive.");
        _factory = factory;
        _options = new RenderTargetPoolOptions
        {
            MaximumRetainedBytes = options.MaximumRetainedBytes,
            MaximumIdleRequests = options.MaximumIdleRequests,
            MaxBufferDimension = options.MaxBufferDimension,
            AfterTargetRegistrationStep = options.AfterTargetRegistrationStep,
            BeforeLeaseRegistration = options.BeforeLeaseRegistration,
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
            SKSurface surface = externalTarget.RawValue;
            GRRecordingContext? context = surface.Context;
            return BeginRequestCore(
                context ?? s_cpuContextIdentity,
                context?.Handle ?? 0,
                externalTarget);
        }

        return BeginImplicitRequest(GraphicsContextFactory.SharedContext);
    }

    /// <summary>
    /// <see cref="BeginRequest"/> without a destination, against a named shared context rather than the live one.
    /// </summary>
    internal RenderTargetPoolRequest BeginImplicitRequest(IGraphicsContext? sharedContext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return BeginRequestCore(
            ResolveImplicitContextIdentity(sharedContext),
            expectedContextHandle: null,
            externalTarget: null);
    }

    /// <summary>The identity a target-less request is bound to.</summary>
    private object ResolveImplicitContextIdentity(IGraphicsContext? sharedContext)
    {
        // A caller-supplied factory picks its own context, and a binding taken from a caller-owned destination
        // or an explicitly named context is the one every surface the pool hands out is checked against.
        // Neither follows the shared context.
        if (_factory is not null || (_hasContext && !ReferenceEquals(_contextIdentity, _implicitBinding)))
            return _hasContext ? _contextIdentity! : _implicitBinding;

        if (!ReferenceEquals(_implicitBinding.Context, sharedContext))
            _implicitBinding = new ImplicitContextBinding(sharedContext);

        return _implicitBinding;
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

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        List<Exception> failures = [];
        RenderTargetPoolRequest? activeRequest = _activeRequest;
        try
        {
            activeRequest?.Dispose();
        }
        catch (Exception ex)
        {
            AppendFailure(failures, ex);
        }

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

    /// <summary>Evicts every unleased retained target and reports the released byte count.</summary>
    /// <remarks>Disposes backend resources, so it must run on the renderer's thread.</remarks>
    internal long ReleaseRetainedTargets()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        long released = _retainedBytes;
        List<Exception> failures = [];
        EvictAllAvailable(_activeRequest, failures);
        ThrowCleanupFailures(failures);
        return released;
    }

    internal PooledRenderTargetLease Acquire(
        RenderTargetPoolRequest request,
        PixelSize deviceSize)
    {
        if (TryAcquire(request, deviceSize, out PooledRenderTargetLease? lease))
            return lease;
        throw ExceedsBufferBudget(request, deviceSize, out int maxDimension)
            ? CreateAllocationFailure(deviceSize, maxDimension)
            : CreateAllocationFailure(deviceSize);
    }

    internal static InvalidOperationException CreateAllocationFailure(
        PixelSize deviceSize,
        int? maxDimension = null)
        => new(maxDimension is { } budget
            ? $"A {deviceSize.Width}x{deviceSize.Height} pixel render target exceeds the {budget} pixels "
              + "this device can attach."
            : $"The render-target factory could not allocate {deviceSize.Width}x{deviceSize.Height} pixels.");

    /// <summary>
    /// Whether <paramref name="deviceSize"/> is past what <paramref name="request"/> may allocate, reporting
    /// the budget it was measured against.
    /// </summary>
    /// <remarks>
    /// A caller consults this to describe a refusal, or to tell one apart from an allocator that merely
    /// declined this time; <see cref="TryAcquire"/> applies it itself.
    /// </remarks>
    internal bool ExceedsBufferBudget(
        RenderTargetPoolRequest request,
        PixelSize deviceSize,
        out int maxDimension)
    {
        maxDimension = ResolveBufferBudget(request);
        return !RenderScaleUtilities.FitsBufferBudget(deviceSize, maxDimension);
    }

    /// <summary>The largest extent <paramref name="request"/>'s allocator may be asked for.</summary>
    /// <remarks>
    /// A device's attachment limit bounds the allocations that reach that device and no others. Only the
    /// pool's own allocator attaches through a shared context, and only from a dispatcher, so only then is
    /// it measured against one; anything else is bounded by the engine ceiling planning already clamped the
    /// density to, and its own allocator declines what it cannot make - <see cref="TryAcquire"/> reports
    /// that as the same decline. A named <see cref="RenderTargetPoolOptions.MaxBufferDimension"/> overrides
    /// both, because it states what this pool may attach whoever allocates it.
    /// </remarks>
    private int ResolveBufferBudget(RenderTargetPoolRequest request)
        => _options.MaxBufferDimension
           ?? (ResolveAttachmentContext(request) is { } context
               ? RenderScaleUtilities.ResolveMaxBufferDimension(context)
               : RenderScaleUtilities.MaxBufferDimension);

    /// <summary>
    /// The shared context this pool's own allocator attaches <paramref name="request"/>'s targets to, or
    /// <see langword="null"/> when nothing it allocates for that request reaches one.
    /// </summary>
    private IGraphicsContext? ResolveAttachmentContext(RenderTargetPoolRequest request)
    {
        // A caller-supplied factory allocates on whatever context it chose - a CPU allocator on none at all -
        // and a CPU-bound request takes this pool's own raster path. Neither attaches through the shared
        // context.
        if (_factory is not null || IsCpuBound(request))
            return null;

        // Everything else lands in RenderTarget.Create, so it attaches wherever that would - and off a
        // dispatcher that is nowhere, because Create rasters there whatever context this request names.
        // Asking Create itself is what keeps the budget and the allocation from answering differently.
        // BeginImplicitRequest names a context in place of the live one, and a request bound to it has to be
        // measured against the device it named rather than against whichever context is live now.
        IGraphicsContext? named = request.ContextIdentity is ImplicitContextBinding binding
            ? binding.Context
            : GraphicsContextFactory.SharedContext;

        // A request opened before any GPU work has happened names nothing, because nothing was installed to
        // name - but Create still builds a device there, and one that attaches less than the engine ceiling
        // would then be asked for a buffer it cannot make. Building it here is what Create does anyway.
        return named is not null
            ? RenderTarget.ResolveCreationContext(named)
            : RenderTarget.ResolveCreationContextForAllocation();
    }

    /// <summary>Whether <paramref name="request"/> is bound to a destination that has no graphics context.</summary>
    /// <remarks>
    /// Read from what the request was opened with rather than from the handle <see cref="ValidateContext"/>
    /// adopts afterwards, so every buffer of one request is measured the same way.
    /// </remarks>
    private static bool IsCpuBound(RenderTargetPoolRequest request)
        => request.ExpectedContextHandle == 0
           || ReferenceEquals(request.ContextIdentity, s_cpuContextIdentity);

    /// <summary>Leases an exact-size target, reporting <see langword="false"/> only when the allocator declines.</summary>
    /// <remarks>Every other failure — a stale slot, a contract-violating factory return — still throws.</remarks>
    /// <param name="clearContents">
    /// Whether the lease must arrive transparent. A caller that defines every pixel of the target before
    /// reading any of it - a full-frame pass whose load op clears, or a shader that provably writes
    /// everywhere - passes <see langword="false"/> and saves the clear and the two layout transitions
    /// around it. The slot's recorded contents stay unknown either way, so the next caller that does want a
    /// blank target still gets one.
    /// </param>
    internal bool TryAcquire(
        RenderTargetPoolRequest request,
        PixelSize deviceSize,
        [NotNullWhen(true)] out PooledRenderTargetLease? lease,
        bool clearContents = true)
    {
        VerifyActive(request);
        if (deviceSize.Width <= 0 || deviceSize.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deviceSize),
                deviceSize,
                "A pooled render target requires a positive device size.");
        }

        // Planning clamps a density to the engine's fixed ceiling so a plan means the same thing on every
        // device, which leaves a request past a smaller device's limit reaching here. Handing that to the
        // backend asks for an attachment it cannot make - undefined behaviour rather than a failed
        // allocation - so it declines instead, and the caller's own degradation contract takes over.
        if (ExceedsBufferBudget(request, deviceSize, out _))
        {
            lease = null;
            return false;
        }

        if (TryTakeAvailable(deviceSize, out TargetSlot? slot))
        {
            TargetSlot reusable = slot!;
            try
            {
                ValidateReusableSlot(reusable, request);
                if (clearContents)
                    reusable.Target.ClearToTransparent();
            }
            catch (Exception ex)
            {
                Evict(reusable, request, failures: null);
                ExceptionDispatchInfo.Capture(ex).Throw();
                throw;
            }

            _reuses++;
            lease = Lease(request, reusable, wasReused: true);
            return true;
        }

        _misses++;
        RenderTarget? target = CreateTarget(deviceSize, request);
        if (target is null && _retainedBytes > 0)
        {
            EvictAllAvailable(request, failures: null);
            target = CreateTarget(deviceSize, request);
        }

        if (target is null)
        {
            lease = null;
            return false;
        }

        bool accepted = false;
        bool targetIsForeign = ReferenceEquals(target, request.ExternalTarget) || _knownTargets.Contains(target);
        bool targetSharesLiveSurface = !targetIsForeign && SharesLiveSurface(target, request);
        try
        {
            SKSurface surface = ValidateFactoryTarget(target, deviceSize, request);
            if (clearContents && !target.HasTransparentContents)
                target.ClearToTransparent();
            long byteSize = GetByteSize(deviceSize);
            long nextOwnedBytes = checked(_ownedBytes + byteSize);
            slot = new TargetSlot(target, surface, deviceSize, byteSize);
            try
            {
                _ownedSlots.Add(slot);
                _options.AfterTargetRegistrationStep?.Invoke(RenderTargetPoolRegistrationStage.OwnedSlot);
                _knownTargets.Add(target);
                _options.AfterTargetRegistrationStep?.Invoke(RenderTargetPoolRegistrationStage.KnownTarget);
                _knownSurfaces.Add(surface);
                _options.AfterTargetRegistrationStep?.Invoke(RenderTargetPoolRegistrationStage.KnownSurface);
            }
            catch
            {
                _knownSurfaces.Remove(surface);
                _knownTargets.Remove(target);
                _ownedSlots.Remove(slot);
                throw;
            }

            _ownedBytes = nextOwnedBytes;
            _creates++;
            accepted = true;
            lease = Lease(request, slot, wasReused: false);
            return true;
        }
        catch (Exception primary)
        {
            if (!accepted)
            {
                if (targetSharesLiveSurface)
                    ReleaseRejectedWrapper(target, request);
                else if (!targetIsForeign)
                    DisposeRejectedTarget(target, request);
            }

            ExceptionDispatchInfo.Capture(primary).Throw();
            throw;
        }
    }

    internal void Release(PooledRenderTargetLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        VerifyLease(lease);
        ReleaseCore(lease);
    }

    internal void DeferRelease(PooledRenderTargetLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        VerifyLease(lease);
        lease.State = PooledRenderTargetLeaseState.Deferred;
    }

    internal void CompleteDeferredRelease(PooledRenderTargetLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!ReferenceEquals(lease.Pool, this))
            throw new InvalidOperationException("The render-target lease belongs to a different pool.");
        if (lease.State == PooledRenderTargetLeaseState.Evicted)
            return;
        if (lease.State != PooledRenderTargetLeaseState.Deferred)
        {
            throw new InvalidOperationException(
                $"The render-target lease cannot complete a deferred release from {lease.State}.");
        }

        TargetSlot slot = lease.Slot;
        if (!ReferenceEquals(slot.ActiveLease, lease) || slot.Generation != lease.Generation)
            throw new InvalidOperationException("The render-target lease generation is stale.");
        ReleaseCore(lease);
    }

    private void ReleaseCore(PooledRenderTargetLease lease)
    {
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

    internal void EvictAfterReleaseFailure(PooledRenderTargetLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!ReferenceEquals(lease.Pool, this))
            throw new InvalidOperationException("The render-target lease belongs to a different pool.");

        Evict(lease.Slot, lease.Request, failures: null);
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
            EvictAllAvailable(request: null, failures);

        if (!_hasContext || !ReferenceEquals(_contextIdentity, contextIdentity))
        {
            _contextIdentity = contextIdentity;
            _graphicsContext = externalTarget?.RawValue.Context ?? contextIdentity as GRRecordingContext;
            _contextHandle = expectedContextHandle ?? 0;
            _hasContext = expectedContextHandle.HasValue;
            _contextGeneration = NextGeneration(_contextGeneration);
        }
        else if (expectedContextHandle.HasValue && _contextHandle != expectedContextHandle.Value)
        {
            EvictAllAvailable(request: null, failures);
            _graphicsContext = externalTarget?.RawValue.Context ?? contextIdentity as GRRecordingContext;
            _contextHandle = expectedContextHandle.Value;
            _hasContext = true;
            _contextGeneration = NextGeneration(_contextGeneration);
        }
        else if (externalTarget?.RawValue.Context is { } graphicsContext)
        {
            _graphicsContext = graphicsContext;
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
        try
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
            _options.BeforeLeaseRegistration?.Invoke();
            request.Register(lease);
            return lease;
        }
        catch
        {
            Evict(slot, request, failures: null);
            throw;
        }
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

    private void EvictAllAvailable(RenderTargetPoolRequest? request, List<Exception>? failures)
    {
        while (_availableLru.First is { } node)
            Evict(node.Value, request, failures);
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

    /// <summary>
    /// Settles a rejected wrapper's own hold on the surface it shares with a live pool slot or with the
    /// caller's destination, so nothing it does later can release that surface out from under them.
    /// </summary>
    /// <remarks>
    /// The two shapes a factory can hand back here need opposite treatment. A reference-counted copy
    /// (<see cref="RenderTarget.ShallowCopy"/>) has to be disposed: that only drops this wrapper's count, and
    /// nothing else ever will, so leaving it strands the surface for the life of the process. A fresh wrapper
    /// holding the sole count on a surface it did not allocate must be neither disposed nor finalized - either
    /// frees memory the live holder is still drawing to - so its finalizer is suppressed instead. Suppression
    /// cannot hide a leak of resources the wrapper does own: the surface belongs to the live holder, and a
    /// target reaching this branch shares that holder's surface rather than a texture it allocated itself.
    /// </remarks>
    private static void ReleaseRejectedWrapper(RenderTarget target, RenderTargetPoolRequest request)
    {
        if (target.SharesSurfaceOwnership)
        {
            DisposeRejectedTarget(target, request);
            return;
        }

        GC.SuppressFinalize(target);
    }

    private static void DisposeRejectedTarget(RenderTarget target, RenderTargetPoolRequest request)
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

    /// <summary>
    /// Whether <paramref name="target"/>'s backing surface is one this pool or the request already holds.
    /// </summary>
    /// <remarks>
    /// A factory can hand back a fresh target instance wrapping a surface something else is still drawing to.
    /// Rejecting it is right, but disposing it would take that surface down with it and leave a live pool slot
    /// or the caller's destination pointing at freed memory, so <see cref="ReleaseRejectedWrapper"/> settles
    /// the rejection instead.
    /// </remarks>
    private bool SharesLiveSurface(RenderTarget target, RenderTargetPoolRequest request)
    {
        try
        {
            SKSurface surface = target.RawValue;
            return ReferenceEquals(surface, request.ExternalSurface) || _knownSurfaces.Contains(surface);
        }
        catch
        {
            // A target that cannot even show its surface shares nothing, so the caller owns its disposal.
            return false;
        }
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

        SKSurface surface = ValidateNewSurface(target, size);
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

        SKSurface surface = ValidateSurfaceIdentityAndViewport(slot.Target, slot.Size);
        if (!ReferenceEquals(surface, slot.Surface))
            throw new InvalidOperationException("A pooled render target changed its backing surface.");
        ValidateContext(surface, request);
    }

    private static SKSurface ValidateNewSurface(RenderTarget target, PixelSize size)
    {
        SKSurface surface = ValidateSurfaceIdentityAndViewport(target, size);
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

    private static SKSurface ValidateSurfaceIdentityAndViewport(RenderTarget target, PixelSize size)
    {
        if (target.IsDisposed || target.Width != size.Width || target.Height != size.Height)
        {
            throw new InvalidOperationException(
                "The render-target factory returned a disposed target or a target whose exact device size is wrong.");
        }

        target.VerifyAccess();
        SKSurface surface = target.RawValue;
        SKRectI deviceClip = surface.Canvas.DeviceClipBounds;
        if (deviceClip.Left != 0
            || deviceClip.Top != 0
            || deviceClip.Width != size.Width
            || deviceClip.Height != size.Height)
        {
            throw new InvalidOperationException(
                "The render-target surface has an incompatible device viewport.");
        }

        return surface;
    }

    private void ValidateContext(SKSurface surface, RenderTargetPoolRequest request)
    {
        GRRecordingContext? actualContext = surface.Context;
        nint actual = actualContext?.Handle ?? 0;
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

        _graphicsContext = actualContext;
    }

    private void VerifyActive(RenderTargetPoolRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        if (!ReferenceEquals(_activeRequest, request) || request.IsDisposed)
            throw new InvalidOperationException("The render-target pool request is no longer active.");
    }

    internal void VerifyLease(PooledRenderTargetLease lease)
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
        => _hasContext
           && ReferenceEquals(_contextIdentity, request.ContextIdentity)
           && _contextGeneration == request.ContextGeneration;

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

    private RenderTarget? CreateTarget(PixelSize deviceSize, RenderTargetPoolRequest request)
        => _factory is null
            ? CreateDefaultTarget(deviceSize, ResolveAllocationContextHandle(request))
            : _factory.Create(GetAllocationDescriptor(deviceSize, request));

    internal RenderTargetAllocationDescriptor GetAllocationDescriptor(
        PixelSize deviceSize,
        RenderTargetPoolRequest request)
    {
        VerifyActive(request);
        return new RenderTargetAllocationDescriptor(
            deviceSize,
            _graphicsContext,
            ResolveAllocationContextHandle(request));
    }

    // Only a request rendering into a caller-owned destination carries a handle of its own. A
    // target-less request on a pool that already bound a context still has to allocate on that
    // context, because every surface the pool hands out is checked against it.
    private nint? ResolveAllocationContextHandle(RenderTargetPoolRequest request)
        => request.ExpectedContextHandle ?? (_hasContext ? _contextHandle : null);

    private static RenderTarget? CreateDefaultTarget(
        PixelSize deviceSize,
        nint? contextHandle)
    {
        if (contextHandle == 0)
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

    private static void AppendFailure(List<Exception> failures, Exception failure)
    {
        if (failure is AggregateException aggregate)
            failures.AddRange(aggregate.Flatten().InnerExceptions);
        else
            failures.Add(failure);
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

    public RenderTarget Target
    {
        get
        {
            Pool.VerifyLease(this);
            return Slot.Target;
        }
    }

    public PixelSize DeviceSize
    {
        get
        {
            Pool.VerifyLease(this);
            return Slot.Size;
        }
    }

    public long Generation { get; }

    public bool WasReused { get; }

    public PooledRenderTargetLeaseState State { get; internal set; } = PooledRenderTargetLeaseState.Leased;

    internal RenderTargetPool Pool { get; }

    internal RenderTargetPoolRequest Request { get; }

    internal RenderTargetPool.TargetSlot Slot { get; }

    public RenderTarget TransferToAcceptedCache()
        => Pool.TransferToAcceptedCache(this);

    internal void DeferRelease()
        => Pool.DeferRelease(this);

    internal void CompleteDeferredRelease()
        => Pool.CompleteDeferredRelease(this);

    public void Dispose()
        => Pool.Release(this);
}
