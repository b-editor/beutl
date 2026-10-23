using System.Diagnostics.CodeAnalysis;

using Beutl.Graphics.Backend;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.Graphics.Rendering.Requests;

/// <summary>
/// Renderer-lifetime owner for exact-size, linear-premultiplied RGBA16F intermediate targets.
/// </summary>
internal sealed partial class RenderTargetPool : IDisposable
{
    private static readonly object s_cpuContextIdentity = new();

    /// <summary>Every constructed, undisposed pool, so context teardown can reach what they retain.</summary>
    /// <remarks>Nothing enumerates the live renderers, so a pool is listed from construction to disposal.</remarks>
    private static readonly HashSet<RenderTargetPool> s_livePools = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// The identity a target-less request on the engine's own allocator uses, and the shared context it was
    /// minted for.
    /// </summary>
    /// <remarks>
    /// Kept together so a replaced shared context gets a new identity, which makes <see cref="BeginSessionCore"/>
    /// evict the previous context's surfaces instead of drawing through a device that is gone.
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
        _options = options;

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
            CleanupFailures.Append(failures, ex);
        }

        if (activeSession is not null)
        {
            foreach (Exception failure in activeSession.CleanupFailures)
                CleanupFailures.Append(failures, failure);
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

    /// <summary>
    /// Evicts every live pool's retained targets while the context is still installed, so their backend releases
    /// reach the live device. Leased targets are left to their holders.
    /// </summary>
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
                CleanupFailures.Append(failures, ex);
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
        _contextGeneration++;
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
        PixelSize deviceSize)
    {
        if (TryAcquire(request, deviceSize, out RenderTargetLease? lease))
            return lease;
        throw ExceedsBufferBudget(request, deviceSize, out int maxDimension)
            ? CreateAllocationFailure(deviceSize, maxDimension)
            : CreateAllocationFailure(deviceSize);
    }

    internal static InvalidOperationException CreateAllocationFailure(
        PixelSize deviceSize,
        int? maxDimension = null)
        => maxDimension is { } budget
            ? new($"A {deviceSize.Width}x{deviceSize.Height} pixel render target exceeds the {budget} pixels "
                  + "this device can attach.")
            : new($"The render-target factory could not allocate {deviceSize.Width}x{deviceSize.Height} pixels.",
                RenderTarget.TakeRefusalReason(deviceSize));

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
    /// Only the pool's own allocator attaching through a shared context is measured against a device; anything
    /// else gets the engine ceiling. A named <see cref="RenderTargetPoolOptions.Budget"/> overrides both.
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
        // A caller-supplied factory and a CPU-bound request never attach through the shared context.
        if (_factory is not null || IsCpuBound(request))
            return null;

        // Everything else attaches wherever RenderTarget.Create would, and a request of BeginImplicitSession is
        // measured against the context it named rather than the live one.
        IGraphicsContext? named = request.ContextIdentity is ImplicitContextBinding binding
            ? binding.Context
            : GraphicsContextFactory.SharedContext;

        // Before any GPU work nothing is named, yet Create still builds a device, so resolve that one.
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
    /// Whether the lease must arrive transparent. A caller that writes every pixel before reading any passes
    /// <see langword="false"/> to skip the clear; the slot's contents stay unknown either way.
    /// </param>
    internal bool TryAcquire(
        RenderTargetLeaseSession request,
        PixelSize deviceSize,
        [NotNullWhen(true)] out RenderTargetLease? lease,
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
            lease = LeaseReusedSlot(slot!, request, clearContents);
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

        lease = AdoptCreatedTarget(target, deviceSize, request, clearContents);
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
        catch
        {
            Evict(reusable, request, failures: null);
            throw;
        }

        _reuses++;
        return Lease(request, reusable);
    }

    private RenderTargetLease AdoptCreatedTarget(
        RenderTarget target,
        PixelSize deviceSize,
        RenderTargetLeaseSession request,
        bool clearContents)
    {
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
            var slot = new TargetSlot(target, surface, deviceSize, byteSize);
            _ownedSlots.Add(slot);
            _knownTargets.Add(target);
            _knownSurfaces.Add(surface);
            _ownedBytes = nextOwnedBytes;
            _creates++;
            accepted = true;
            return Lease(request, slot);
        }
        catch
        {
            if (!accepted)
            {
                if (targetSharesLiveSurface)
                    ReleaseRejectedWrapper(target, request);
                else if (!targetIsForeign)
                    DisposeRejectedTarget(target, request);
            }

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
            _contextGeneration++;
        }
        else if (expectedContextHandle.HasValue && _contextHandle != expectedContextHandle.Value)
        {
            EvictAllAvailable(request: null, failures);
            _graphicsContext = externalTarget?.RawValue.Context ?? contextIdentity as GRRecordingContext;
            _contextHandle = expectedContextHandle.Value;
            _hasContext = true;
            _contextGeneration++;
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

    private RenderTargetLease Lease(
        RenderTargetLeaseSession request,
        TargetSlot slot)
    {
        var lease = new RenderTargetLease(request, slot);
        slot.ActiveLease = lease;
        _leasedTargets++;
        _peakLiveTargets = Math.Max(_peakLiveTargets, _leasedTargets);
        request.Register(lease);
        return lease;
    }

    /// <summary>
    /// Settles a rejected wrapper that shares a live surface: a <see cref="RenderTarget.ShallowCopy"/> is disposed
    /// to drop its count; a fresh wrapper must not free the surface, so only its finalizer is suppressed.
    /// </summary>
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
        => checked((long)size.Width * size.Height * 8);

    private RenderTarget? CreateTarget(PixelSize deviceSize, RenderTargetLeaseSession request)
        => _factory is null
            ? CreateDefaultTarget(deviceSize, ResolveAllocationContextHandle(request))
            : _factory.Create(GetAllocationDescriptor(deviceSize, request));

    private RenderTargetAllocationDescriptor GetAllocationDescriptor(
        PixelSize deviceSize,
        RenderTargetLeaseSession request)
        => new(
            deviceSize,
            _graphicsContext,
            ResolveAllocationContextHandle(request));

    // Only a request rendering into a caller-owned destination carries a handle of its own. A
    // target-less request on a pool that already bound a context still has to allocate on that
    // context, because every surface the pool hands out is checked against it.
    private nint? ResolveAllocationContextHandle(RenderTargetLeaseSession request)
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
        => CleanupFailures.ThrowIfAny(failures, "One or more pooled render targets failed to dispose.");

    private sealed class CpuRenderTarget(SKSurface surface, PixelSize size)
        : RenderTarget(surface, size.Width, size.Height);
}
