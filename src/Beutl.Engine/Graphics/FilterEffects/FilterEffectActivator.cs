using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

internal delegate ProgramCacheLease<CachedSkRuntimeEffect> SkRuntimeEffectProgramAcquirer(
    EffectTarget target,
    string source);

public sealed class FilterEffectActivator : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger("FilterEffectActivator");
    private readonly SkRuntimeEffectProgramAcquirer? _injectedProgramAcquirer;
    private readonly Vector? _deviceGridOffset;
    private readonly DrawableBrushMaterializer? _drawableBrushMaterializer;
    private readonly bool _useExecutorManagedCanvas;
    private readonly RenderTargetLeaseSession? _renderTargetLeaseSession;
    private ProgramCache<CachedSkRuntimeEffect>? _ownedProgramCache;
    private Dictionary<EffectTarget, PendingSkiaTarget>? _pendingSkiaTargets;
    private bool _customEffectBoundaryMaterialized;

    public FilterEffectActivator(
        EffectTargets targets,
        SKImageFilterBuilder builder,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        float outputScale = 1f,
        float workingScale = 1f,
        float maxWorkingScale = float.PositiveInfinity,
        DrawableBrushMaterializer? drawableBrushMaterializer = null)
        : this(
            targets,
            builder,
            intent,
            purpose,
            outputScale,
            workingScale,
            maxWorkingScale,
            acquireProgram: null,
            deviceGridOffset: null,
            ownsProgramCache: true,
            drawableBrushMaterializer,
            useExecutorManagedCanvas: false,
            renderTargetLeaseSession: null)
    {
    }

    internal FilterEffectActivator(
        EffectTargets targets,
        SKImageFilterBuilder builder,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        float outputScale,
        float workingScale,
        float maxWorkingScale,
        Vector deviceGridOffset,
        DrawableBrushMaterializer? drawableBrushMaterializer = null,
        bool useExecutorManagedCanvas = false,
        RenderTargetLeaseSession? renderTargetLeaseSession = null)
        : this(
            targets,
            builder,
            intent,
            purpose,
            outputScale,
            workingScale,
            maxWorkingScale,
            acquireProgram: null,
            deviceGridOffset,
            ownsProgramCache: true,
            drawableBrushMaterializer,
            useExecutorManagedCanvas,
            renderTargetLeaseSession)
    {
    }

    internal FilterEffectActivator(
        EffectTargets targets,
        SKImageFilterBuilder builder,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        float outputScale,
        float workingScale,
        float maxWorkingScale,
        Vector deviceGridOffset,
        SkRuntimeEffectProgramAcquirer acquireProgram,
        DrawableBrushMaterializer? drawableBrushMaterializer = null,
        bool useExecutorManagedCanvas = false,
        RenderTargetLeaseSession? renderTargetLeaseSession = null)
        : this(
            targets,
            builder,
            intent,
            purpose,
            outputScale,
            workingScale,
            maxWorkingScale,
            acquireProgram ?? throw new ArgumentNullException(nameof(acquireProgram)),
            deviceGridOffset,
            ownsProgramCache: false,
            drawableBrushMaterializer,
            useExecutorManagedCanvas,
            renderTargetLeaseSession)
    {
    }

    private FilterEffectActivator(
        EffectTargets targets,
        SKImageFilterBuilder builder,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        float outputScale,
        float workingScale,
        float maxWorkingScale,
        SkRuntimeEffectProgramAcquirer? acquireProgram,
        Vector? deviceGridOffset,
        bool ownsProgramCache,
        DrawableBrushMaterializer? drawableBrushMaterializer,
        bool useExecutorManagedCanvas,
        RenderTargetLeaseSession? renderTargetLeaseSession)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(builder);
        if (!Enum.IsDefined(intent))
            throw new ArgumentOutOfRangeException(nameof(intent), intent, "The render intent is invalid.");
        if (!Enum.IsDefined(purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "The render request purpose is invalid.");

        Builder = builder;
        CurrentTargets = targets;
        OutputScale = SanitizePositiveFinite(outputScale, nameof(outputScale));
        WorkingScale = SanitizePositiveFinite(workingScale, nameof(workingScale));
        MaxWorkingScale = SanitizeCeiling(maxWorkingScale, nameof(maxWorkingScale));
        Intent = intent;
        Purpose = purpose;
        _deviceGridOffset = deviceGridOffset;
        _drawableBrushMaterializer = drawableBrushMaterializer;
        _useExecutorManagedCanvas = useExecutorManagedCanvas;
        _renderTargetLeaseSession = renderTargetLeaseSession;
        if (!ownsProgramCache)
        {
            _injectedProgramAcquirer = acquireProgram
                ?? throw new ArgumentNullException(nameof(acquireProgram));
        }
    }

    public SKImageFilterBuilder Builder { get; }

    public EffectTargets CurrentTargets { get; }

    /// <summary>The render request's output scale <c>s_out</c>. Sanitized to positive-finite.</summary>
    public float OutputScale { get; }

    /// <summary>
    /// Working density <c>w</c> for buffer allocation. Reduced in place by <see cref="Flush"/>
    /// when the dimension clamp fires. Sanitized to positive-finite.
    /// </summary>
    public float WorkingScale { get; private set; }

    /// <summary>Working-scale ceiling forwarded into nested canvases. NaN or non-positive becomes +Inf (no ceiling).</summary>
    public float MaxWorkingScale { get; }

    /// <summary>Gets the explicit preview or delivery classification for this execution.</summary>
    public RenderIntent Intent { get; }

    /// <summary>Gets the explicit request purpose for this execution.</summary>
    public RenderRequestPurpose Purpose { get; }

    private static float SanitizeCeiling(float value, string name)
    {
        float sanitized = RenderScaleUtilities.SanitizeMaxWorkingScale(value);
        return sanitized != value ? LogAndFallback(value, name, sanitized) : sanitized;
    }

    private static float SanitizePositiveFinite(float value, string name)
    {
        if (float.IsFinite(value) && value > 0f)
            return value;
        s_logger.LogWarning("FilterEffectActivator: {Param} ({Value}) is not positive-finite; falling back to 1.0.",
            name, value);
        return 1f;
    }

    private static float LogAndFallback(float value, string name, float fallback)
    {
        s_logger.LogWarning("FilterEffectActivator: {Param} ({Value}) is not positive; falling back to {Fallback}.",
            name, value, fallback);
        return fallback;
    }

    private ProgramCacheLease<CachedSkRuntimeEffect> AcquireOwnedProgram(
        EffectTarget target,
        string source)
    {
        ProgramCache<CachedSkRuntimeEffect> cache =
            _ownedProgramCache ??= SkRuntimeEffectProgramCache.Create();
        RenderTarget destination = target.RenderTarget
            ?? throw new InvalidOperationException(
                "A legacy shader program requires a materialized execution destination.");
        return SkRuntimeEffectProgramCache.AcquireForDestination(
            cache,
            destination,
            source);
    }

    private SkRuntimeEffectProgramAcquirer GetProgramAcquirer()
        => _injectedProgramAcquirer ?? AcquireOwnedProgram;

    public void Dispose()
    {
        _ownedProgramCache?.Dispose();
    }

    public void Flush(bool force = true)
    {
        bool hasFilter = Builder.HasFilter();
        if (!force && !hasFilter)
        {
            _pendingSkiaTargets = null;
            return;
        }

        using var paint = hasFilter ? new SKPaint() : null;
        paint?.ImageFilter = Builder.GetFilter();

        // A forced flush without pending Skia work is the legacy CustomEffect compatibility
        // boundary. A forced materialization of a Skia chain must retain its canonical device
        // footprint; otherwise unchanged color effects lose edge coverage at fractional scales.
        bool imperativeSegmentBoundary = force && !hasFilter;

        var flushTargets = new Dictionary<EffectTarget, FlushTarget>();
        // Re-clamp against the physical runtime footprint. A retained raster can be wider than
        // semantic Bounds after a custom effect moves or shrinks the target.
        for (int i = 0; i < CurrentTargets.Count; i++)
        {
            EffectTarget target = CurrentTargets[i];
            Rect allocationBounds = hasFilter ? target.OriginalBounds : target.Bounds;
            if (IsEmptyBounds(allocationBounds) || !IsAllocatableBounds(allocationBounds))
                continue;

            FlushTarget flushTarget = ResolveFlushTarget(target, hasFilter);
            if (!IsAllocatableBounds(flushTarget.PhysicalBounds))
                continue;

            flushTargets.Add(target, flushTarget);
            Rect budgetBounds = imperativeSegmentBoundary
                ? new Rect(default, target.Bounds.Size)
                : ResolveDeviceRoundingSource(target, flushTarget, hasFilter);
            float fit = imperativeSegmentBoundary
                ? RenderScaleUtilities.ClampWorkingScaleToBufferBudget(
                    budgetBounds,
                    WorkingScale)
                : RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(
                    budgetBounds.Translate(target.DeviceGridOffset),
                    WorkingScale);
            if (fit < WorkingScale)
            {
                s_logger.LogWarning(
                    "Working scale clamped {From} -> {To} to keep an effect buffer within the GPU axis limit (bounds {Bounds}).",
                    WorkingScale, fit, budgetBounds);
                WorkingScale = fit;
            }
        }

        for (int i = 0; i < CurrentTargets.Count; i++)
        {
            EffectTarget target = CurrentTargets[i];
            Rect allocationBounds = hasFilter ? target.OriginalBounds : target.Bounds;
            if (IsEmptyBounds(allocationBounds))
            {
                // An empty target has nothing to render; drop it in every mode (it is not an
                // allocation failure), so degenerate glyph/GPU no-op cases do not fail delivery.
                target.Dispose();
                CurrentTargets.RemoveAt(i);
                i--;
                continue;
            }

            if (!IsAllocatableBounds(allocationBounds)
                || !flushTargets.TryGetValue(target, out FlushTarget flushTarget))
            {
                // Non-finite/negative bounds cannot be allocated (and would crash the native
                // allocator), so never reach it: delivery fails fast, preview drops the target.
                s_logger.LogWarning(
                    "Effect flush buffer allocation failed (non-allocatable bounds {Bounds}); preview drops this target, delivery render fails fast.",
                    allocationBounds);
                target.Dispose();
                ThrowIfDeliveryAllocationFailure(
                    $"Effect flush buffer allocation failed (non-allocatable bounds {allocationBounds}).");
                CurrentTargets.RemoveAt(i);
                i--;
                continue;
            }

            float w = WorkingScale;
            if (!hasFilter
                && imperativeSegmentBoundary
                && CanReuseLegacyTarget(target, w))
                continue;

            bool preserveLegacyRasterPlacement = imperativeSegmentBoundary;
            Vector allocationGridOffset = preserveLegacyRasterPlacement
                ? _deviceGridOffset ?? default
                : target.DeviceGridOffset;
            Rect deviceRoundingSource = imperativeSegmentBoundary
                ? target.Bounds
                : ResolveDeviceRoundingSource(target, flushTarget, hasFilter);
            PixelRect canonicalDeviceBounds = CustomFilterEffectContext.DeviceBufferBounds(
                deviceRoundingSource.Translate(allocationGridOffset), w);
            PixelRect deviceBounds;
            Vector outputDeviceGridOffset;
            if (preserveLegacyRasterPlacement)
            {
                (int width, int height) = CustomFilterEffectContext.DeviceBufferSize(
                    target.Bounds,
                    w);
                deviceBounds = new PixelRect(
                    canonicalDeviceBounds.Position,
                    new PixelSize(width, height));
                outputDeviceGridOffset = deviceBounds
                    .ToRect(w)
                    .Position - target.Bounds.Position;
            }
            else
            {
                deviceBounds = canonicalDeviceBounds;
                outputDeviceGridOffset = target.DeviceGridOffset;
            }
            if (hasFilter && !preserveLegacyRasterPlacement)
                VerifyFilteredDeviceBounds(target, deviceBounds, w);
            Rect rasterBounds = deviceBounds
                .ToRect(w)
                .Translate(-outputDeviceGridOffset);
            EffectTarget? newTarget = AllocateFlushTarget(
                target.Bounds,
                w,
                deviceBounds,
                outputDeviceGridOffset,
                preserveLegacyRasterPlacement);

            if (newTarget != null)
            {
                try
                {
                    Vector rasterTranslation = DeviceGridAlignment.ResolveRasterTranslation(
                        deviceBounds,
                        outputDeviceGridOffset,
                        w);
                    using ImmediateCanvas canvas = CreateExecutionCanvas(
                        newTarget.RenderTarget!,
                        w,
                        rasterBounds.Size);
                    canvas.Clear();
                    using (canvas.PushTransform(
                               Matrix.CreateTranslation(
                                   flushTarget.InputBounds.X + rasterTranslation.X,
                                   flushTarget.InputBounds.Y + rasterTranslation.Y)))
                    // The layer must be bounded by the content being filtered. Without explicit
                    // bounds Skia sizes the filter's layer from the clip and samples the area
                    // outside the drawn content, which is uninitialized device memory — a blur
                    // (DropShadow, Blur) then pulls those undefined values into the result as NaN.
                    using (paint != null
                               ? canvas.PushFilterLayer(paint, new Rect(default, flushTarget.InputBounds.Size))
                               : default)
                    {
                        target.Draw(canvas);
                    }
                }
                catch
                {
                    newTarget.Dispose();
                    throw;
                }

                newTarget.OriginalBounds = target.OriginalBounds;
                CurrentTargets[i] = newTarget;
                target.Dispose();
            }
            else
            {
                // The layer would silently vanish from the output otherwise — make the failure visible.
                s_logger.LogWarning(
                    "Effect flush buffer allocation failed ({Width}x{Height} px, w {WorkingScale}, bounds {Bounds}); preview drops this target, delivery render fails fast.",
                    deviceBounds.Width, deviceBounds.Height, w, flushTarget.PhysicalBounds);
                target.Dispose();

                ThrowIfDeliveryAllocationFailure(
                    $"Effect flush buffer allocation failed ({deviceBounds.Width}x{deviceBounds.Height} px, w {w}, bounds {flushTarget.PhysicalBounds}).");

                CurrentTargets.RemoveAt(i);
                i--;
            }
        }

        _pendingSkiaTargets = null;
        Builder.Clear();
    }

    /// <summary>
    /// Allocates one flush buffer, through the caller's lease session when there is one.
    /// </summary>
    /// <remarks>
    /// A configured <see cref="IRenderTargetFactory"/> is reachable only through the session, and its targets
    /// may come from a context the global allocator knows nothing about, so going around it here would both
    /// ignore the caller's allocation policy and mix surfaces from two contexts inside one flush.
    /// </remarks>
    private EffectTarget? AllocateFlushTarget(
        Rect bounds,
        float w,
        PixelRect deviceBounds,
        Vector deviceGridOffset,
        bool preserveLegacyRasterPlacement)
    {
        if (_renderTargetLeaseSession is { HasTargetFactory: true } leaseSession)
        {
            RenderTargetLease? lease = leaseSession.TryAcquire(deviceBounds.Size);
            if (lease is null)
                return null;

            try
            {
                return EffectTarget.FromLease(
                    lease,
                    bounds,
                    EffectiveScale.At(w),
                    deviceBounds,
                    deviceGridOffset,
                    preserveLegacyRasterPlacement);
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }

        using RenderTarget? surface = RenderTarget.Create(deviceBounds.Width, deviceBounds.Height);
        return surface is null
            ? null
            : new EffectTarget(
                surface,
                bounds,
                EffectiveScale.At(w),
                deviceBounds,
                deviceGridOffset,
                preserveLegacyRasterPlacement);
    }

    internal void CompletePolicyBoundary(bool materializationRequired)
    {
        // A CustomEffect already consumed the policy through its forced pre-callback Flush.
        // Re-forcing after the callback would discard backing that legacy code intentionally
        // retained while moving or shrinking only Bounds. Pending Skia work still flushes.
        Flush(materializationRequired && !_customEffectBoundaryMaterialized);
    }

    private FlushTarget ResolveFlushTarget(EffectTarget target, bool hasFilter)
    {
        if (!hasFilter)
        {
            // A forced no-filter flush is the compatibility boundary for imperative CustomEffect
            // callbacks. Materialize semantic input without exposing a renderer-owned apron;
            // callback-created targets keep their separate legacy local-buffer contract.
            return new FlushTarget(target.Bounds, target.Bounds);
        }

        Rect inputBounds;
        Rect physicalBounds;
        if (_pendingSkiaTargets?.TryGetValue(target, out PendingSkiaTarget? pending) == true)
        {
            inputBounds = pending.InputBounds;
            physicalBounds = pending.PhysicalBounds;
        }
        else
        {
            inputBounds = target.Bounds;
            physicalBounds = target.RasterBounds.Translate(
                target.OriginalBounds.Position - target.Bounds.Position);
        }

        // Skia bounds callbacks are authored in OriginalBounds' local coordinate space. Keep the
        // union in that space through clamping and device rounding; moving it into global logical
        // coordinates first can erase the extra pixel contributed by a fractional local origin.
        Rect localSemanticBounds = target.Bounds.Translate(
            target.OriginalBounds.Position - target.Bounds.Position);
        return new FlushTarget(
            inputBounds,
            physicalBounds
                .Union(target.OriginalBounds)
                .Union(localSemanticBounds));
    }

    // The filtered union is built in OriginalBounds' local space, but device rounding must happen once
    // in global space: a locally rounded rect re-anchored by a separately rounded offset cannot reproduce
    // the rounding the semantic device bounds use. The explicit Bounds union keeps containment exact
    // instead of relying on the local round trip being bit-exact in float.
    private static Rect ResolveDeviceRoundingSource(
        EffectTarget target,
        FlushTarget flushTarget,
        bool hasFilter)
        => hasFilter
            ? flushTarget.PhysicalBounds
                .Translate(target.Bounds.Position - target.OriginalBounds.Position)
                .Union(target.Bounds)
            : flushTarget.PhysicalBounds;

    private static void VerifyFilteredDeviceBounds(
        EffectTarget target,
        PixelRect deviceBounds,
        float density)
    {
        PixelRect semanticDeviceBounds = PixelRect.FromRect(
            target.Bounds.Translate(target.DeviceGridOffset),
            density);
        if (!Contains(deviceBounds, semanticDeviceBounds))
        {
            throw new InvalidOperationException(
                "A filtered physical footprint must contain its semantic device bounds.");
        }
    }

    private static bool CanReuseLegacyTarget(EffectTarget target, float density)
    {
        if (!target.PreserveLegacyRasterPlacement
            || target.Scale.IsUnbounded
            || target.Scale.Value != density
            || target.RenderTarget is not { } renderTarget)
        {
            return false;
        }

        (int width, int height) = CustomFilterEffectContext.DeviceBufferSize(
            target.Bounds,
            density);
        return renderTarget.Width == width && renderTarget.Height == height;
    }

    private static bool Contains(PixelRect outer, PixelRect inner)
        => outer.X <= inner.X
           && outer.Y <= inner.Y
           && outer.Right >= inner.Right
           && outer.Bottom >= inner.Bottom;

    /// <summary>
    /// Makes sure every current target has chain bookkeeping, keeping what an in-progress chain accumulated.
    /// </summary>
    /// <remarks>
    /// A Skia item runs author code that may re-enter <see cref="Activate"/> or <see cref="Flush"/>, both of
    /// which drop this map and can replace the targets it was keyed by. Entries are therefore added rather
    /// than the map rebuilt, so calling this again after author code has run restores a dropped map and covers
    /// a target that appeared, without resetting a chain that survived.
    /// </remarks>
    private void BeginSkiaChain()
    {
        _pendingSkiaTargets ??= new Dictionary<EffectTarget, PendingSkiaTarget>();
        foreach (EffectTarget target in CurrentTargets)
        {
            if (_pendingSkiaTargets.ContainsKey(target))
                continue;

            Rect physicalBounds = target.RasterBounds.Translate(
                target.OriginalBounds.Position - target.Bounds.Position);
            // OriginalBounds cannot serve as the anchor frame: a stage the fallback executor allocated
            // itself begins a chain with OriginalBounds == Bounds, which anchors the chain at zero.
            _pendingSkiaTargets.Add(
                target,
                new PendingSkiaTarget(
                    target.Bounds,
                    physicalBounds,
                    new Rect(default, target.Bounds.Size)));
        }
    }

    private void ThrowIfDeliveryAllocationFailure(string message)
    {
        if (Intent == RenderIntent.Delivery)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static bool IsAllocatableBounds(Rect bounds)
        => double.IsFinite(bounds.X)
           && double.IsFinite(bounds.Y)
           && double.IsFinite(bounds.Width)
           && double.IsFinite(bounds.Height)
           && bounds.Width > 0
           && bounds.Height > 0;

    // A finite, non-negative target with a zero extent: renderable-but-empty, distinct from the
    // negative/non-finite bounds IsAllocatableBounds rejects as an allocation failure.
    private static bool IsEmptyBounds(Rect bounds)
        => double.IsFinite(bounds.Width)
           && double.IsFinite(bounds.Height)
           && bounds.Width >= 0
           && bounds.Height >= 0
           && (bounds.Width == 0 || bounds.Height == 0);

    // 最小単位である'IFEItem'の数がわからないので 'count'は'nullable'
    public void Apply(FilterEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (CurrentTargets.Count == 0) return;
        context.PrepareStandaloneResourcesForExecution();

        foreach (IFEItem item in context._items)
        {
            switch (item)
            {
                case IFEItem_Skia skia:
                    {
                        BeginSkiaChain();
                        skia.Accepts(this, Builder);
                        // Author code just ran and may have gone through Activate() or Flush(), either of
                        // which drops the bookkeeping this loop is about to read.
                        BeginSkiaChain();
                        // A deferred-bound Skia item resolves its matrix once from the combined
                        // execution-time target bounds (the first TransformBounds call fixes it),
                        // because its origin depends on input bounds a preceding custom effect may
                        // only re-target at execution time. Every target then maps with that matrix.
                        if (skia.ResolveBoundsAtExecutionTime)
                            _ = item.TransformBounds(CurrentTargets.CalculateBounds());

                        foreach (EffectTarget t in CurrentTargets)
                        {
                            PendingSkiaTarget pending = _pendingSkiaTargets![t];
                            pending.PhysicalBounds = item.TransformBounds(pending.PhysicalBounds);
                            pending.AnchorFrame = item.TransformBounds(pending.AnchorFrame);
                            t.Bounds = item.TransformBounds(t.Bounds);
                            t.OriginalBounds = item.TransformBounds(t.OriginalBounds);
                            // The chain's execution frame is anchored at InputBounds.Position, which
                            // must stay equal to the displacement this item's accumulated mapping
                            // gives the chain-start Bounds.Position. A translation-invariant item
                            // preserves that displacement, so this is a no-op there; a matrix item
                            // moves Bounds relative to the anchor frame and has to re-anchor with it.
                            pending.InputBounds = new Rect(
                                t.Bounds.Position - pending.AnchorFrame.Position,
                                pending.InputBounds.Size);
                        }

                        break;
                    }
                case IFEItem_Custom custom:
                    {
                        Flush();
                        if (CurrentTargets.Count == 0) return;
                        _customEffectBoundaryMaterialized = true;

                        var customContext = new CustomFilterEffectContext(
                            CurrentTargets,
                            Intent,
                            Purpose,
                            OutputScale,
                            WorkingScale,
                            MaxWorkingScale,
                            _deviceGridOffset,
                            _drawableBrushMaterializer,
                            _useExecutorManagedCanvas,
                            _renderTargetLeaseSession);
                        custom.Accepts(customContext);

                        foreach (EffectTarget t in CurrentTargets)
                        {
                            t.OriginalBounds = t.Bounds.WithX(0).WithY(0);
                        }

                        break;
                    }
                case FEItem_Shader shader:
                    {
                        Flush(false);
                        if (CurrentTargets.Count == 0) return;
                        FilterEffectStageFallbackExecutor.ApplyShader(
                            CurrentTargets,
                            shader.Description,
                            OutputScale,
                            WorkingScale,
                            MaxWorkingScale,
                            Intent,
                            Purpose,
                            GetProgramAcquirer(),
                            _renderTargetLeaseSession);
                        break;
                    }
                case FEItem_Geometry geometry:
                    {
                        Flush(false);
                        if (CurrentTargets.Count == 0) return;
                        FilterEffectStageFallbackExecutor.ApplyGeometry(
                            CurrentTargets,
                            geometry.Description,
                            OutputScale,
                            WorkingScale,
                            MaxWorkingScale,
                            Intent,
                            Purpose,
                            _renderTargetLeaseSession);
                        break;
                    }
            }
        }

        if (context._renderTimeItems.Count <= 0) return;

        Flush(false);
        if (CurrentTargets.Count == 0) return;
        using var ctx = new FilterEffectContext(CurrentTargets.CalculateBounds(), OutputScale, WorkingScale);

        foreach (IFEItem item in context._renderTimeItems)
        {
            ctx._items.Add(item);
        }

        Apply(ctx);
    }

    public SKImageFilter? Activate(FilterEffectContext context)
    {
        // A no-op Flush still drops the pending-Skia bookkeeping, which the caller's own in-progress chain
        // still needs when it authored this call from a Skia factory.
        Dictionary<EffectTarget, PendingSkiaTarget>? pendingSkiaTargets =
            Builder.HasFilter() ? null : _pendingSkiaTargets;
        Flush(false);
        _pendingSkiaTargets = pendingSkiaTargets;

        using EffectTargets cloned = CurrentTargets.Clone();
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            cloned,
            builder,
            Intent,
            Purpose,
            OutputScale,
            WorkingScale,
            MaxWorkingScale,
            _deviceGridOffset
                ?? (cloned.Count > 0 ? cloned[0].DeviceGridOffset : default),
            GetProgramAcquirer(),
            _drawableBrushMaterializer,
            _useExecutorManagedCanvas,
            _renderTargetLeaseSession);

        activator.Apply(context);
        activator.Flush(false);

        SKImageFilter? filter = builder.GetFilter();
        if (filter != null) return filter;

        foreach (EffectTarget t in activator.CurrentTargets)
        {
            if (t.RenderTarget == null) continue;

            SKSurface innerSurface = t.RenderTarget.Value;
            using SKImage skImage = innerSurface.Snapshot();

            Rect rasterBounds = t.RasterBounds;
            SKImageFilter image = SKImageFilter.CreateImage(
                skImage,
                new SKRect(0, 0, skImage.Width, skImage.Height),
                rasterBounds.ToSKRect(),
                SKSamplingOptions.Default);

            filter = filter == null ? image : SKImageFilter.CreateCompose(filter, image);
        }

        return filter;
    }

    private sealed class PendingSkiaTarget(
        Rect inputBounds,
        Rect physicalBounds,
        Rect anchorFrame)
    {
        public Rect InputBounds { get; set; } = inputBounds;

        public Rect PhysicalBounds { get; set; } = physicalBounds;

        /// <summary>
        /// The chain-start frame: the origin at the chain-start <see cref="EffectTarget.Bounds"/> size,
        /// mapped by every item alongside them. Subtracting its position from the mapped Bounds position
        /// leaves the chain-start Bounds position under the accumulated linear part, which is the anchor
        /// the flush frame needs. The matching size is what makes that subtraction cancel: a bounds map
        /// displaces two rects by the same amount only while their sizes agree.
        /// </summary>
        public Rect AnchorFrame { get; set; } = anchorFrame;
    }

    private readonly record struct FlushTarget(
        Rect InputBounds,
        Rect PhysicalBounds);

    private ImmediateCanvas CreateExecutionCanvas(
        RenderTarget target,
        float density,
        Size logicalSize)
    {
        ImmediateCanvas canvas;
        if (_useExecutorManagedCanvas)
        {
            canvas = ImmediateCanvas.CreateExecutorManaged(
                target,
                density,
                MaxWorkingScale,
                logicalSize,
                Intent);
            canvas.ConfigureCustomEffectExecution();
        }
        else
        {
            canvas = new ImmediateCanvas(
                target,
                density,
                MaxWorkingScale,
                logicalSize,
                Intent);
        }

        canvas.DrawableBrushMaterializer = _drawableBrushMaterializer;
        return canvas;
    }
}
