using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

/// <summary>
/// Applies a recorded <see cref="FilterEffectContext"/> to a set of <see cref="EffectTargets"/>.
/// </summary>
/// <remarks>
/// <para>
/// The public constructor builds a <i>standalone</i> activator, which allocates its own intermediates from
/// the process-wide shared graphics context. That is the right answer only when the activator belongs to no
/// render — there is no caller allocation policy to honour.
/// </para>
/// <para>
/// Inside a custom effect callback there is one, so call
/// <see cref="CustomFilterEffectContext.CreateActivator"/> instead of constructing an activator: it carries
/// the running render's lease session, and so allocates through a caller-supplied
/// <see cref="IRenderTargetFactory"/> rather than drawing factory-made inputs into a shared-context buffer.
/// </para>
/// </remarks>
public sealed class FilterEffectActivator : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger("FilterEffectActivator");
    private readonly SkRuntimeEffectProgramAcquirer? _injectedProgramAcquirer;
    private readonly Vector? _deviceGridOffset;
    private readonly DrawableBrushMaterializer? _drawableBrushMaterializer;
    private readonly bool _useExecutorManagedCanvas;
    private readonly RenderTargetLeaseSession? _renderTargetLeaseSession;
    private readonly int? _maxBufferDimension;
    private readonly Rect? _targetDomain;
    private ProgramCache<CachedSkRuntimeEffect>? _ownedProgramCache;
    private Dictionary<EffectTarget, PendingSkiaTarget>? _pendingSkiaTargets;
    private bool _customEffectBoundaryMaterialized;

    /// <param name="drawableBrushMaterializer">
    /// The hook that rasterizes a <see cref="DrawableBrush.Resource"/> an effect paints with, or
    /// <see langword="null"/> when the caller applies no drawable brush. Stated rather than defaulted: left
    /// implicit, a <see cref="DrawableBrush"/> resolves to transparent instead of to its content.
    /// </param>
    public FilterEffectActivator(
        EffectTargets targets,
        SKImageFilterBuilder builder,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        DrawableBrushMaterializer? drawableBrushMaterializer,
        float outputScale = 1f,
        float workingScale = 1f,
        float maxWorkingScale = float.PositiveInfinity,
        Rect? targetDomain = null)
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
            renderTargetLeaseSession: null,
            maxBufferDimension: null,
            targetDomain)
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
        RenderTargetLeaseSession? renderTargetLeaseSession = null,
        int? maxBufferDimension = null,
        Rect? targetDomain = null)
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
            renderTargetLeaseSession,
            maxBufferDimension,
            targetDomain)
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
        RenderTargetLeaseSession? renderTargetLeaseSession = null,
        int? maxBufferDimension = null,
        Rect? targetDomain = null)
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
            renderTargetLeaseSession,
            maxBufferDimension,
            targetDomain)
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
        RenderTargetLeaseSession? renderTargetLeaseSession,
        int? maxBufferDimension,
        Rect? targetDomain)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(builder);
        if (!Enum.IsDefined(intent))
            throw new ArgumentOutOfRangeException(nameof(intent), intent, "The render intent is invalid.");
        if (!Enum.IsDefined(purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "The render request purpose is invalid.");
        if (maxBufferDimension is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxBufferDimension),
                maxBufferDimension,
                "The maximum buffer dimension must be positive.");
        }

        _maxBufferDimension = maxBufferDimension;
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
        _targetDomain = targetDomain;
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

    /// <summary>
    /// Gets the largest device extent an allocation from this activator may have, on both axes.
    /// </summary>
    /// <remarks>
    /// Resolved per call rather than in the constructor: an activator can outlive the moment the graphics
    /// context first answers, and until it does the engine ceiling stands in for the device's own limit.
    /// </remarks>
    public int MaxBufferDimension
        => _maxBufferDimension ?? RenderScaleUtilities.ResolveMaxBufferDimension();

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
                "A effectItem shader program requires a materialized execution destination.");
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

    /// <summary>The widest target list a flush plan is held on the stack for.</summary>
    /// <remarks>
    /// A flush almost always sees one target - a split effect is what produces more, and the widest any
    /// built-in reaches across the graphics suite is nine. Past this width the plan is heap-allocated,
    /// which costs one array where the map this replaced cost three objects at every width.
    /// </remarks>
    private const int StackFlushPlanLimit = 16;

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

        // A forced flush without pending Skia work is the effect-item CustomEffect compatibility
        // boundary. A forced materialization of a Skia chain must retain its canonical device
        // footprint; otherwise unchanged color effects lose edge coverage at fractional scales.
        bool imperativeSegmentBoundary = force && !hasFilter;

        int count = CurrentTargets.Count;
        Span<FlushTarget?> plan = count <= StackFlushPlanLimit
            ? stackalloc FlushTarget?[StackFlushPlanLimit]
            : new FlushTarget?[count];
        plan = plan[..count];
        PlanFlush(plan, hasFilter, imperativeSegmentBoundary);
        MaterializeFlush(plan, paint, hasFilter, imperativeSegmentBoundary);

        _pendingSkiaTargets = null;
        Builder.Clear();
    }

    /// <summary>
    /// Resolves each target's flush frame, and settles <see cref="WorkingScale"/> for the whole flush.
    /// </summary>
    /// <remarks>
    /// Separate from materialization because the dimension clamp belongs to the flush rather than to one
    /// target: a target whose buffer would exceed the axis limit lowers the density every other target is
    /// then allocated at, so none may be allocated until all of them have been measured. The plan is
    /// positional - slot <c>i</c> answers for the target at index <c>i</c> - which is what lets
    /// materialization read it while removing entries from the list it was measured against.
    /// </remarks>
    private void PlanFlush(Span<FlushTarget?> plan, bool hasFilter, bool imperativeSegmentBoundary)
    {
        for (int i = 0; i < plan.Length; i++)
        {
            plan[i] = null;
            EffectTarget target = CurrentTargets[i];
            // Re-clamp against the physical runtime footprint. A retained raster can be wider than
            // semantic Bounds after a custom effect moves or shrinks the target.
            Rect allocationBounds = hasFilter ? target.OriginalBounds : target.Bounds;
            if (RenderScaleUtilities.IsEmptyBounds(allocationBounds)
                || !RenderScaleUtilities.IsAllocatableBounds(allocationBounds))
                continue;

            FlushTarget flushTarget = ResolveFlushTarget(target, hasFilter);
            if (!RenderScaleUtilities.IsAllocatableBounds(flushTarget.PhysicalBounds))
                continue;

            plan[i] = flushTarget;
            ClampWorkingScaleToFlushBudget(target, flushTarget, hasFilter, imperativeSegmentBoundary);
        }
    }

    /// <summary>Lowers <see cref="WorkingScale"/> when this target's flush buffer would exceed the axis limit.</summary>
    private void ClampWorkingScaleToFlushBudget(
        EffectTarget target,
        FlushTarget flushTarget,
        bool hasFilter,
        bool imperativeSegmentBoundary)
    {
        Rect budgetBounds = imperativeSegmentBoundary
            ? new Rect(default, target.Bounds.Size)
            : ResolveDeviceRoundingSource(target, flushTarget, hasFilter);
        int budgetDimension = MaxBufferDimension;
        float fit = imperativeSegmentBoundary
            ? RenderScaleUtilities.ClampWorkingScaleToDeviceBufferBudget(
                budgetBounds,
                WorkingScale,
                budgetDimension)
            : RenderScaleUtilities.ClampWorkingScaleToExactDeviceBufferBudget(
                budgetBounds.Translate(target.DeviceGridOffset),
                WorkingScale,
                budgetDimension);
        if (fit >= WorkingScale)
            return;

        s_logger.LogWarning(
            "Working scale clamped {From} -> {To} to keep an effect buffer within the {Limit} px GPU axis limit (bounds {Bounds}).",
            WorkingScale, fit, budgetDimension, budgetBounds);
        WorkingScale = fit;
    }

    /// <summary>Allocates a buffer for every planned target and draws the pending chain into it.</summary>
    private void MaterializeFlush(
        ReadOnlySpan<FlushTarget?> plan,
        SKPaint? paint,
        bool hasFilter,
        bool imperativeSegmentBoundary)
    {
        // 'planned' counts targets consumed rather than list positions, so it keeps naming the slot this
        // target was measured into while removals move the list index back under it.
        for (int i = 0, planned = 0; i < CurrentTargets.Count; i++, planned++)
        {
            EffectTarget target = CurrentTargets[i];
            Rect allocationBounds = hasFilter ? target.OriginalBounds : target.Bounds;
            if (RenderScaleUtilities.IsEmptyBounds(allocationBounds))
            {
                // An empty target has nothing to render; drop it in every mode (it is not an
                // allocation failure), so degenerate glyph/GPU no-op cases do not fail delivery.
                target.Dispose();
                CurrentTargets.RemoveAt(i);
                i--;
                continue;
            }

            if (plan[planned] is not { } flushTarget)
            {
                ReportUnallocatableBounds(target, i, allocationBounds);
                i--;
                continue;
            }

            float w = WorkingScale;
            if (!hasFilter
                && imperativeSegmentBoundary
                && CanReuseEffectItemTarget(target, w))
                continue;

            FlushGeometry geometry = ResolveFlushGeometry(
                target,
                flushTarget,
                w,
                hasFilter,
                imperativeSegmentBoundary);
            EffectTarget? newTarget = EffectTargetAllocation.Allocate(
                _renderTargetLeaseSession,
                target.Bounds,
                w,
                geometry.DeviceBounds,
                geometry.DeviceGridOffset,
                preserveImperativeRasterPlacement: imperativeSegmentBoundary);
            if (newTarget is null)
            {
                ReportRefusedFlushBuffer(target, i, geometry.DeviceBounds, w, flushTarget.PhysicalBounds);
                i--;
                continue;
            }

            DrawIntoFlushTarget(target, newTarget, flushTarget, geometry, paint, w);
            newTarget.OriginalBounds = target.OriginalBounds;
            CurrentTargets[i] = newTarget;
            target.Dispose();
        }
    }

    /// <summary>Reports a target whose own bounds no allocator could be asked for.</summary>
    private void ReportUnallocatableBounds(EffectTarget target, int index, Rect allocationBounds)
    {
        // Non-finite/negative bounds cannot be allocated (and would crash the native
        // allocator), so never reach it: delivery fails fast, preview drops the target.
        s_logger.LogWarning(
            "Effect flush buffer allocation failed (non-allocatable bounds {Bounds}); preview drops this target, delivery render fails fast.",
            allocationBounds);
        DropUnflushableTarget(
            target,
            index,
            $"Effect flush buffer allocation failed (non-allocatable bounds {allocationBounds}).");
    }

    /// <summary>Reports a flush buffer the allocator would not give.</summary>
    private void ReportRefusedFlushBuffer(
        EffectTarget target,
        int index,
        PixelRect deviceBounds,
        float w,
        Rect physicalBounds)
    {
        // The layer would silently vanish from the output otherwise — make the failure visible.
        s_logger.LogWarning(
            "Effect flush buffer allocation failed ({Width}x{Height} px, w {WorkingScale}, bounds {Bounds}); preview drops this target, delivery render fails fast.",
            deviceBounds.Width, deviceBounds.Height, w, physicalBounds);
        DropUnflushableTarget(
            target,
            index,
            $"Effect flush buffer allocation failed ({deviceBounds.Width}x{deviceBounds.Height} px, w {w}, bounds {physicalBounds}).");
    }

    /// <summary>Reports a target that cannot be flushed, and takes it out of the chain.</summary>
    /// <remarks>
    /// Delivery throws before the removal, so a render that was asked for exact output never returns a
    /// frame missing a layer; preview drops the target and records that the content went missing.
    /// </remarks>
    private void DropUnflushableTarget(EffectTarget target, int index, string message)
    {
        target.Dispose();
        ThrowIfDeliveryAllocationFailure(message);
        _renderTargetLeaseSession?.MarkContentDropped();
        CurrentTargets.RemoveAt(index);
    }

    /// <summary>Where a flush buffer sits on the device grid, and the raster frame it is drawn in.</summary>
    private readonly record struct FlushGeometry(
        PixelRect DeviceBounds,
        Vector DeviceGridOffset,
        Rect RasterBounds);

    private FlushGeometry ResolveFlushGeometry(
        EffectTarget target,
        FlushTarget flushTarget,
        float w,
        bool hasFilter,
        bool imperativeSegmentBoundary)
    {
        bool preserveImperativeRasterPlacement = imperativeSegmentBoundary;
        Vector allocationGridOffset = preserveImperativeRasterPlacement
            ? _deviceGridOffset ?? default
            : target.DeviceGridOffset;
        Rect deviceRoundingSource = imperativeSegmentBoundary
            ? target.Bounds
            : ResolveDeviceRoundingSource(target, flushTarget, hasFilter);
        PixelRect canonicalDeviceBounds = CustomFilterEffectContext.DeviceBufferBounds(
            deviceRoundingSource.Translate(allocationGridOffset), w);
        PixelRect deviceBounds;
        Vector outputDeviceGridOffset;
        if (preserveImperativeRasterPlacement)
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

        if (hasFilter && !preserveImperativeRasterPlacement)
            VerifyFilteredDeviceBounds(target, deviceBounds, w);

        return new FlushGeometry(
            deviceBounds,
            outputDeviceGridOffset,
            deviceBounds
                .ToRect(w)
                .Translate(-outputDeviceGridOffset));
    }

    /// <summary>Draws the target, through the pending Skia chain, into its freshly allocated buffer.</summary>
    private void DrawIntoFlushTarget(
        EffectTarget source,
        EffectTarget destination,
        FlushTarget flushTarget,
        in FlushGeometry geometry,
        SKPaint? paint,
        float w)
    {
        try
        {
            Vector rasterTranslation = DeviceGridAlignment.ResolveRasterTranslation(
                geometry.DeviceBounds,
                geometry.DeviceGridOffset,
                w);
            using ImmediateCanvas canvas = CreateExecutionCanvas(
                destination.RenderTarget!,
                w,
                geometry.RasterBounds.Size);
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
                source.Draw(canvas);
            }
        }
        catch
        {
            destination.Dispose();
            throw;
        }
    }

    internal void CompletePolicyBoundary(bool materializationRequired)
    {
        // A CustomEffect already consumed the policy through its forced pre-callback Flush.
        // Re-forcing after the callback would discard backing that effect-item code intentionally
        // retained while moving or shrinking only Bounds. Pending Skia work still flushes.
        Flush(materializationRequired && !_customEffectBoundaryMaterialized);
    }

    private FlushTarget ResolveFlushTarget(EffectTarget target, bool hasFilter)
    {
        if (!hasFilter)
        {
            // A forced no-filter flush is the compatibility boundary for imperative CustomEffect
            // callbacks. Materialize semantic input without exposing a renderer-owned apron;
            // callback-created targets keep their separate effect-item local-buffer contract.
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
        if (!deviceBounds.Contains(semanticDeviceBounds))
        {
            throw new InvalidOperationException(
                "A filtered physical footprint must contain its semantic device bounds.");
        }
    }

    private static bool CanReuseEffectItemTarget(EffectTarget target, float density)
    {
        if (!target.PreserveImperativeRasterPlacement
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
                    AccumulateSkiaItem(skia);
                    break;
                case IFEItem_Custom custom:
                    // A custom effect runs author code against materialized targets, so the pending
                    // chain has to be forced out before it can see them.
                    Flush();
                    if (CurrentTargets.Count == 0) return;
                    RunCustomEffectItem(custom);
                    break;
                case FEItem_Shader shader:
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
                case FEItem_Geometry geometry:
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

        ApplyRenderTimeItems(context);
    }

    /// <summary>Adds one Skia item to the pending chain and maps every target's bounds through it.</summary>
    private void AccumulateSkiaItem(IFEItem_Skia skia)
    {
        // A deferred-bound Skia item's origin depends on input bounds a preceding custom
        // effect may only re-target at execution time, so this activation resolves its own
        // matrix from the combined execution-time target bounds before the filter is built.
        // Both the built filter and every bounds mapping below then use that one matrix.
        IFEItem_Skia effectiveItem = skia is IFEItem_DeferredBounds deferred
            ? deferred.ResolveForActivation(CurrentTargets.CalculateBounds())
            : skia;

        BeginSkiaChain();
        effectiveItem.Accepts(this, Builder);
        // Author code just ran and may have gone through Activate() or Flush(), either of
        // which drops the bookkeeping this loop is about to read.
        BeginSkiaChain();

        foreach (EffectTarget t in CurrentTargets)
        {
            PendingSkiaTarget pending = _pendingSkiaTargets![t];
            pending.PhysicalBounds = effectiveItem.TransformBounds(pending.PhysicalBounds);
            pending.AnchorFrame = effectiveItem.TransformBounds(pending.AnchorFrame);
            t.Bounds = effectiveItem.TransformBounds(t.Bounds);
            t.OriginalBounds = effectiveItem.TransformBounds(t.OriginalBounds);
            // The chain's execution frame is anchored at InputBounds.Position, which
            // must stay equal to the displacement this item's accumulated mapping
            // gives the chain-start Bounds.Position. A translation-invariant item
            // preserves that displacement, so this is a no-op there; a matrix item
            // moves Bounds relative to the anchor frame and has to re-anchor with it.
            pending.InputBounds = new Rect(
                t.Bounds.Position - pending.AnchorFrame.Position,
                pending.InputBounds.Size);
        }
    }

    /// <summary>Hands the materialized targets to author code, and re-anchors what it left behind.</summary>
    private void RunCustomEffectItem(IFEItem_Custom custom)
    {
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
            _renderTargetLeaseSession,
            _maxBufferDimension,
            _targetDomain);
        custom.Accepts(customContext);

        foreach (EffectTarget t in CurrentTargets)
        {
            t.OriginalBounds = t.Bounds.WithX(0).WithY(0);
        }
    }

    /// <summary>Re-enters with the items that could only be recorded once the render was under way.</summary>
    private void ApplyRenderTimeItems(FilterEffectContext context)
    {
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
            _renderTargetLeaseSession,
            targetDomain: _targetDomain);

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
                Intent,
                density,
                MaxWorkingScale,
                logicalSize);
        }

        canvas.DrawableBrushMaterializer = _drawableBrushMaterializer;
        return canvas;
    }
}
