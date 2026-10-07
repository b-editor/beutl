using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

using Beutl.Graphics.Backend;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.Graphics.Rendering.Requests;

/// <summary>
/// Renderer-lifetime owner for exact-size, premultiplied RGBA16F composition and effect targets.
/// </summary>
internal sealed partial class RenderTargetPool : IDisposable
{
    private static readonly object s_cpuContextIdentity = new();

    /// <summary>Every constructed, undisposed pool, so context teardown can reach what they retain.</summary>
    /// <remarks>
    /// A pool belongs to one renderer and nothing enumerates the live renderers, so a single teardown hook on
    /// <see cref="GraphicsContextFactory"/> would have nothing to call. Only the constructor adds and only
    /// <see cref="Dispose"/> removes, so a pool is listed for exactly as long as it can be holding a target.
    /// </remarks>
    private static readonly HashSet<RenderTargetPool> s_livePools = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// The identity a target-less request on the engine's own allocator uses, and the shared context it was
    /// minted for.
    /// </summary>
    /// <remarks>
    /// The two live in one object so a request can never take an identity minted for one context while another
    /// is live. <see cref="GraphicsContextFactory.Shutdown"/> is public, so the shared context is replaceable
    /// while the pool still holds the previous one's surfaces; minting a new identity for the new context is
    /// what makes <see cref="BeginSessionCore"/> evict them, rather than validating them against the handle the
    /// replaced context reported and then clearing and drawing through a device that is gone.
    /// </remarks>
    private sealed class ImplicitContextBinding(IGraphicsContext? context)
    {
        public IGraphicsContext? Context { get; } = context;
    }

    private readonly IRenderTargetFactory? _factory;
    private readonly RenderTargetPoolOptions _options;
    private readonly Dictionary<(PixelSize Size, RenderTargetPixelFormat Format), LinkedList<TargetSlot>> _availableBuckets = [];
    private readonly LinkedList<TargetSlot> _availableLru = [];
    private readonly HashSet<TargetSlot> _ownedSlots = [];
    private readonly HashSet<RenderTarget> _knownTargets = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<SKSurface> _knownSurfaces = new(ReferenceEqualityComparer.Instance);
    private RenderTargetLeaseSession? _activeSession;
    private ImplicitContextBinding _implicitBinding = new(null);
    private object? _contextIdentity;
    private GRRecordingContext? _graphicsContext;
    private nint _contextHandle;
    private bool _hasContext;
    private long _requestEpoch;
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
        BufferDimensionBudget.ThrowIfUninitialized(options.Budget, nameof(options));

        _factory = factory;
        _options = new RenderTargetPoolOptions
        {
            MaximumRetainedBytes = options.MaximumRetainedBytes,
            MaximumIdleRequests = options.MaximumIdleRequests,
            Budget = options.Budget,
            AfterTargetRegistrationStep = options.AfterTargetRegistrationStep,
            BeforeLeaseRegistration = options.BeforeLeaseRegistration,
        };

        lock (s_livePools)
            s_livePools.Add(this);
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

    public bool HasTargetFactory => _factory is not null;

    public RenderTargetLeaseSession BeginSession(
        RenderIntent intent,
        RenderTarget? externalTarget = null)
    {
        if (!Enum.IsDefined(intent))
            throw new ArgumentOutOfRangeException(nameof(intent));
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (externalTarget is not null)
        {
            externalTarget.VerifyAccess();
            SKSurface surface = externalTarget.RawValue;
            GRRecordingContext? context = surface.Context;
            return BeginSessionCore(
                intent,
                context ?? s_cpuContextIdentity,
                context?.Handle ?? 0,
                externalTarget);
        }

        return BeginImplicitSession(intent, GraphicsContextFactory.SharedContext);
    }

    /// <summary>
    /// <see cref="BeginSession"/> without a destination, against a named shared context rather than the live one.
    /// </summary>
    internal RenderTargetLeaseSession BeginImplicitSession(
        RenderIntent intent,
        IGraphicsContext? sharedContext)
    {
        if (!Enum.IsDefined(intent))
            throw new ArgumentOutOfRangeException(nameof(intent));
        ObjectDisposedException.ThrowIf(_disposed, this);
        return BeginSessionCore(
            intent,
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

    public RenderTargetLeaseSession BeginSessionForContext(
        RenderIntent intent,
        object contextIdentity,
        nint expectedContextHandle,
        RenderTarget? externalTarget = null)
    {
        if (!Enum.IsDefined(intent))
            throw new ArgumentOutOfRangeException(nameof(intent));
        ArgumentNullException.ThrowIfNull(contextIdentity);
        ObjectDisposedException.ThrowIf(_disposed, this);
        externalTarget?.VerifyAccess();
        return BeginSessionCore(intent, contextIdentity, expectedContextHandle, externalTarget);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        lock (s_livePools)
            s_livePools.Remove(this);

        List<Exception> failures = [];
        RenderTargetLeaseSession? activeSession = _activeSession;
        try
        {
            activeSession?.Dispose();
        }
        catch (Exception ex)
        {
            AppendFailure(failures, ex);
        }

        if (activeSession is not null)
        {
            foreach (Exception failure in activeSession.CleanupFailures)
                AppendFailure(failures, failure);
        }
        _activeSession = null;

        foreach (TargetSlot slot in _ownedSlots.ToArray())
            Evict(slot, request: null, failures);

        _availableBuckets.Clear();
        _availableLru.Clear();
        _knownTargets.Clear();
        _knownSurfaces.Clear();
        ThrowCleanupFailures(failures);
    }

    /// <summary>Evicts every live pool's retained targets, for a caller about to destroy the context.</summary>
    /// <remarks>
    /// A pool learns a context is gone only from the next request naming a different one, which after a
    /// shutdown is never. By then the eviction's backend releases cannot even be deferred - with no shared
    /// context installed <see cref="GpuResourceReclaimQueue.TryDefer"/> declines - so each one reaches a
    /// device <c>vkDestroyDevice</c> has already taken, and the backend drops it rather than destroy against
    /// a dangling handle. Calling this while the context is still installed is what keeps those releases on
    /// the live device. Leased targets stay: their holder releases them, and a release that lands after
    /// teardown is the backend's own guard to refuse.
    /// </remarks>
    internal static void RetireRetainedTargetsBeforeContextTeardown()
    {
        RenderTargetPool[] pools;
        lock (s_livePools)
            pools = [.. s_livePools];

        List<Exception> failures = [];
        foreach (RenderTargetPool pool in pools)
        {
            try
            {
                pool.RetireCurrentContext();
            }
            catch (ObjectDisposedException)
            {
                // Disposed after the snapshot was taken; it released what it held on the way out.
            }
            catch (Exception ex)
            {
                AppendFailure(failures, ex);
            }
        }

        ThrowCleanupFailures(failures);
    }

    /// <summary>Evicts every unleased retained target and reports the released byte count.</summary>
    /// <remarks>Disposes backend resources, so it must run on the renderer's thread.</remarks>
    internal long ReleaseRetainedTargets()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        long released = _retainedBytes;
        List<Exception> failures = [];
        EvictAllAvailable(_activeSession, failures);
        ThrowCleanupFailures(failures);
        return released;
    }

    /// <summary>Invalidates the current context before its queued deferred leases are reclaimed.</summary>
    internal void RetireCurrentContext()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _contextGeneration = NextGeneration(_contextGeneration);
        _contextIdentity = null;
        _graphicsContext = null;
        _contextHandle = 0;
        _hasContext = false;

        List<Exception> failures = [];
        EvictAllAvailable(_activeSession, failures);
        ThrowCleanupFailures(failures);
    }

    internal RenderTargetLease Acquire(
        RenderTargetLeaseSession request,
        PixelSize deviceSize,
        RenderTargetPixelFormat pixelFormat = RenderTargetPixelFormat.SrgbPremultipliedRgba16Float)
    {
        if (TryAcquire(request, deviceSize, out RenderTargetLease? lease, pixelFormat: pixelFormat))
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
        RenderTargetLeaseSession request,
        PixelSize deviceSize,
        out int maxDimension)
    {
        BufferDimensionBudget budget = ResolveBufferBudget(request);
        maxDimension = budget.MaxDimension;
        return !budget.Fits(deviceSize);
    }

    /// <summary>The largest extent <paramref name="request"/>'s allocator may be asked for.</summary>
    /// <remarks>
    /// A device's attachment limit bounds the allocations that reach that device and no others. Only the
    /// pool's own allocator attaches through a shared context, and only from a dispatcher, so only then is
    /// it measured against one; anything else is bounded by the engine ceiling planning already clamped the
    /// density to, and its own allocator declines what it cannot make - <see cref="TryAcquire"/> reports
    /// that as the same decline. A named <see cref="RenderTargetPoolOptions.Budget"/> overrides both,
    /// because it states what this pool may attach regardless of which allocator makes the allocation.
    /// </remarks>
    private BufferDimensionBudget ResolveBufferBudget(RenderTargetLeaseSession request)
        => _options.Budget
           ?? (ResolveAttachmentContext(request) is { } context
               ? BufferDimensionBudget.ForDevice(context)
               : BufferDimensionBudget.EngineCeiling);

    /// <summary>
    /// The shared context this pool's own allocator attaches <paramref name="request"/>'s targets to, or
    /// <see langword="null"/> when nothing it allocates for that request reaches one.
    /// </summary>
    private IGraphicsContext? ResolveAttachmentContext(RenderTargetLeaseSession request)
    {
        // A caller-supplied factory allocates on whatever context it chose - a CPU allocator on none at all -
        // and a CPU-bound request takes this pool's own raster path. Neither attaches through the shared
        // context.
        if (_factory is not null || IsCpuBound(request))
            return null;

        // Everything else lands in RenderTarget.Create, so it attaches wherever that would - and off the
        // render thread that is nowhere, because Create rasters there whatever context this request names.
        // Asking Create itself is what keeps the budget and the allocation from answering differently.
        // BeginImplicitSession names a context in place of the live one, and a request bound to it has to be
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
    private static bool IsCpuBound(RenderTargetLeaseSession request)
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
        RenderTargetLeaseSession request,
        PixelSize deviceSize,
        [NotNullWhen(true)] out RenderTargetLease? lease,
        bool clearContents = true,
        RenderTargetPixelFormat pixelFormat = RenderTargetPixelFormat.SrgbPremultipliedRgba16Float)
    {
        VerifyActive(request);
        _ = pixelFormat.GetColorSpace();
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

        if (TryTakeAvailable(deviceSize, pixelFormat, out TargetSlot? slot))
        {
            lease = LeaseReusedSlot(slot!, request, clearContents);
            return true;
        }

        _misses++;
        RenderTarget? target = CreateTarget(deviceSize, request, pixelFormat);
        if (target is null && _retainedBytes > 0)
        {
            EvictAllAvailable(request, failures: null);
            target = CreateTarget(deviceSize, request, pixelFormat);
        }

        if (target is null)
        {
            lease = null;
            return false;
        }

        lease = AdoptCreatedTarget(target, deviceSize, request, clearContents, pixelFormat);
        return true;
    }

    private RenderTargetLease LeaseReusedSlot(
        TargetSlot reusable,
        RenderTargetLeaseSession request,
        bool clearContents)
    {
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
        return Lease(request, reusable);
    }

    private RenderTargetLease AdoptCreatedTarget(
        RenderTarget target,
        PixelSize deviceSize,
        RenderTargetLeaseSession request,
        bool clearContents,
        RenderTargetPixelFormat pixelFormat)
    {
        bool accepted = false;
        bool targetIsForeign = ReferenceEquals(target, request.ExternalTarget) || _knownTargets.Contains(target);
        bool targetSharesLiveSurface = !targetIsForeign && SharesLiveSurface(target, request);
        try
        {
            SKSurface surface = ValidateFactoryTarget(target, deviceSize, request, pixelFormat);
            if (clearContents && !target.HasTransparentContents)
                target.ClearToTransparent();
            long byteSize = GetByteSize(deviceSize);
            long nextOwnedBytes = checked(_ownedBytes + byteSize);
            var slot = new TargetSlot(target, surface, deviceSize, byteSize, pixelFormat);
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
            return Lease(request, slot);
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

    internal void Release(RenderTargetLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        VerifyReleasableLease(lease);
        ReleaseCore(lease);
    }

    internal void DeferRelease(RenderTargetLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        VerifyLease(lease);
        lease.State = RenderTargetLeaseState.Deferred;
    }

    internal void CompleteDeferredRelease(RenderTargetLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        VerifyPoolOwnership(lease);
        if (lease.State == RenderTargetLeaseState.Evicted)
            return;
        if (lease.State != RenderTargetLeaseState.Deferred)
        {
            throw new InvalidOperationException(
                $"The render-target lease cannot complete a deferred release from {lease.State}.");
        }

        VerifySlotHolds(lease);
        ReleaseCore(lease);
    }

    private void ReleaseCore(RenderTargetLease lease)
    {
        TargetSlot slot = lease.Slot;
        lease.State = RenderTargetLeaseState.Released;
        slot.ActiveLease = null;
        slot.LastUsedEpoch = _requestEpoch;
        _leasedTargets--;

        if (_disposed || !IsCurrentContext(lease.Session) || slot.Target.IsDisposed)
        {
            lease.State = RenderTargetLeaseState.Evicted;
            Evict(slot, lease.Session, failures: null);
            return;
        }

        AddAvailable(slot);
        TrimToByteBudget(lease.Session);
        if (!_ownedSlots.Contains(slot))
            lease.State = RenderTargetLeaseState.Evicted;
    }

    internal RenderTarget TransferToAcceptedCache(RenderTargetLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        VerifyLease(lease);

        TargetSlot slot = lease.Slot;
        slot.ActiveLease = null;
        lease.State = RenderTargetLeaseState.CacheTransferred;
        _leasedTargets--;
        RemoveOwnedSlot(slot);
        return slot.Target;
    }

    internal void EndSession(RenderTargetLeaseSession request)
    {
        if (ReferenceEquals(_activeSession, request))
            _activeSession = null;
    }

    internal void EvictAfterReleaseFailure(RenderTargetLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        VerifyPoolOwnership(lease);

        Evict(lease.Slot, lease.Session, failures: null, failedLease: lease);
    }

    private RenderTargetLeaseSession BeginSessionCore(
        RenderIntent intent,
        object contextIdentity,
        nint? expectedContextHandle,
        RenderTarget? externalTarget)
    {
        if (_activeSession is not null)
        {
            throw new InvalidOperationException(
                "Concurrent render-target allocation sessions on one renderer are unsupported.");
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
        var session = new RenderTargetLeaseSession(
            this,
            intent,
            contextIdentity,
            _contextGeneration,
            expectedContextHandle,
            externalTarget);
        _activeSession = session;
        TrimIdle(session);
        return session;
    }

    private static long NextGeneration(long current)
        => current == long.MaxValue ? 1 : current + 1;

    private RenderTargetLease Lease(
        RenderTargetLeaseSession request,
        TargetSlot slot)
    {
        try
        {
            var lease = new RenderTargetLease(request, slot);
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
    private static void ReleaseRejectedWrapper(RenderTarget target, RenderTargetLeaseSession request)
    {
        if (target.SharesSurfaceOwnership)
        {
            DisposeRejectedTarget(target, request);
            return;
        }

        GC.SuppressFinalize(target);
    }

    private static void DisposeRejectedTarget(RenderTarget target, RenderTargetLeaseSession request)
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

    private RenderTarget? CreateTarget(PixelSize deviceSize, RenderTargetLeaseSession request, RenderTargetPixelFormat pixelFormat)
        => _factory is null
            ? CreateDefaultTarget(deviceSize, ResolveAllocationContextHandle(request), pixelFormat)
            : _factory.Create(GetAllocationDescriptor(deviceSize, request, pixelFormat));

    internal RenderTargetAllocationDescriptor GetAllocationDescriptor(
        PixelSize deviceSize,
        RenderTargetLeaseSession request,
        RenderTargetPixelFormat pixelFormat = RenderTargetPixelFormat.SrgbPremultipliedRgba16Float)
    {
        VerifyActive(request);
        return new RenderTargetAllocationDescriptor(
            deviceSize,
            _graphicsContext,
            ResolveAllocationContextHandle(request),
            pixelFormat);
    }

    // Only a request rendering into a caller-owned destination carries a handle of its own. A
    // target-less request on a pool that already bound a context still has to allocate on that
    // context, because every surface the pool hands out is checked against it.
    private nint? ResolveAllocationContextHandle(RenderTargetLeaseSession request)
        => request.ExpectedContextHandle ?? (_hasContext ? _contextHandle : null);

    private static RenderTarget? CreateDefaultTarget(
        PixelSize deviceSize,
        nint? contextHandle,
        RenderTargetPixelFormat pixelFormat)
    {
        if (contextHandle == 0)
        {
            SKSurface? surface = SKSurface.Create(new SKImageInfo(
                deviceSize.Width,
                deviceSize.Height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                pixelFormat.GetColorSpace().SKColorSpace));
            return surface is null
                ? null
                : new CpuRenderTarget(surface, deviceSize);
        }

        return RenderTarget.Create(deviceSize.Width, deviceSize.Height, pixelFormat);
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
        {
            foreach (Exception inner in aggregate.Flatten().InnerExceptions)
                AppendFailure(failures, inner);
        }
        else if (!failures.Contains(failure))
        {
            failures.Add(failure);
        }
    }

    private sealed class CpuRenderTarget(SKSurface surface, PixelSize size)
        : RenderTarget(surface, size.Width, size.Height);
}
