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
    private readonly IReadOnlyDictionary<FilterEffectBrush, LoweredBrush>? _brushes;
    private ProgramCache<CachedSkRuntimeEffect>? _ownedProgramCache;
    private Dictionary<EffectTarget, PendingSkiaTarget>? _pendingSkiaTargets;
    private bool _customEffectBoundaryMaterialized;

    public FilterEffectActivator(
        EffectTargets targets,
        SKImageFilterBuilder builder,
        float outputScale = 1f,
        float workingScale = 1f,
        float maxWorkingScale = float.PositiveInfinity,
        RenderIntent intent = RenderIntent.Preview)
        : this(
            targets,
            builder,
            intent,
            RenderRequestPurpose.Auxiliary,
            outputScale,
            workingScale,
            maxWorkingScale)
    {
    }

    internal FilterEffectActivator(
        EffectTargets targets,
        SKImageFilterBuilder builder,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        float outputScale = 1f,
        float workingScale = 1f,
        float maxWorkingScale = float.PositiveInfinity)
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
            ownsProgramCache: true)
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
        IReadOnlyDictionary<FilterEffectBrush, LoweredBrush>? brushes)
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
            brushes)
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
        IReadOnlyDictionary<FilterEffectBrush, LoweredBrush>? brushes = null)
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
            brushes)
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
        IReadOnlyDictionary<FilterEffectBrush, LoweredBrush>? brushes = null)
    {
        _brushes = brushes;
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
        bool legacyCompatibilityBoundary = force && !hasFilter;

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
            Rect budgetBounds = legacyCompatibilityBoundary
                ? new Rect(default, target.Bounds.Size)
                : ResolveDeviceRoundingSource(target, flushTarget, hasFilter);
            float fit = legacyCompatibilityBoundary
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
                && legacyCompatibilityBoundary
                && CanReuseLegacyTarget(target, w))
                continue;

            bool preserveLegacyRasterPlacement = legacyCompatibilityBoundary;
            Vector allocationGridOffset = preserveLegacyRasterPlacement
                ? _deviceGridOffset ?? default
                : target.DeviceGridOffset;
            Rect deviceRoundingSource = legacyCompatibilityBoundary
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
            using RenderTarget? surface = RenderTarget.Create(
                deviceBounds.Width,
                deviceBounds.Height);

            if (surface != null)
            {
                Vector rasterTranslation = DeviceGridAlignment.ResolveRasterTranslation(
                    deviceBounds,
                    outputDeviceGridOffset,
                    w);
                using (var canvas = new ImmediateCanvas(surface, w, MaxWorkingScale,
                           logicalSize: rasterBounds.Size, intent: Intent))
                {
                    canvas.Clear();
                    using (canvas.PushTransform(
                               Matrix.CreateTranslation(
                                   flushTarget.InputBounds.X + rasterTranslation.X,
                                   flushTarget.InputBounds.Y + rasterTranslation.Y)))
                    using (paint != null ? canvas.PushPaint(paint) : default)
                    {
                        target.Draw(canvas);
                    }
                }

                var newTarget = new EffectTarget(
                    surface,
                    target.Bounds,
                    EffectiveScale.At(w),
                    deviceBounds,
                    outputDeviceGridOffset,
                    preserveLegacyRasterPlacement)
                {
                    OriginalBounds = target.OriginalBounds
                };
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

    private void BeginSkiaChain()
    {
        if (_pendingSkiaTargets is not null)
            return;

        _pendingSkiaTargets = new Dictionary<EffectTarget, PendingSkiaTarget>();
        foreach (EffectTarget target in CurrentTargets)
        {
            Rect physicalBounds = target.RasterBounds.Translate(
                target.OriginalBounds.Position - target.Bounds.Position);
            _pendingSkiaTargets.Add(
                target,
                new PendingSkiaTarget(target.Bounds, physicalBounds));
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
        try
        {
            ApplyCore(context);
        }
        // A swallowed pre-lowering failure usually surfaces here as the unlowered-DrawableBrush guard, which names
        // neither the nested effect nor the real cause.
        catch (Exception ex) when (context.NestedBrushLoweringFailure is { } loweringFailure
                                   && !ReferenceEquals(
                                       ex.Data[FilterEffectContext.NestedBrushLoweringFailureKey],
                                       loweringFailure))
        {
            ex.Data[FilterEffectContext.NestedBrushLoweringFailureKey] = loweringFailure;
            throw;
        }
    }

    private void ApplyCore(FilterEffectContext context)
    {
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
                        // A deferred-bound Skia item resolves its matrix from the execution-time
                        // target bounds: its origin depends on the input bounds, which a preceding
                        // custom effect may re-target only at execution time.
                        if (skia.ResolveBoundsAtExecutionTime)
                        {
                            // The matrix is resolved once from the combined execution-time target
                            // bounds (the first TransformBounds call fixes it); every target then
                            // transforms with that same matrix, and InputBounds is anchored to the
                            // mapped Bounds - OriginalBounds difference so the flush draws the
                            // source where the resolved matrix maps it.
                            Rect combinedBounds = CurrentTargets.CalculateBounds();
                            _ = item.TransformBounds(combinedBounds);
                            foreach (EffectTarget t in CurrentTargets)
                            {
                                PendingSkiaTarget pending = _pendingSkiaTargets![t];
                                pending.PhysicalBounds = item.TransformBounds(pending.PhysicalBounds);
                                t.Bounds = item.TransformBounds(t.Bounds);
                                t.OriginalBounds = item.TransformBounds(t.OriginalBounds);
                                pending.InputBounds = new Rect(
                                    t.Bounds.Position - t.OriginalBounds.Position,
                                    pending.InputBounds.Size);
                            }
                        }
                        else
                        {
                            foreach (EffectTarget t in CurrentTargets)
                            {
                                PendingSkiaTarget pending = _pendingSkiaTargets![t];
                                pending.PhysicalBounds = item.TransformBounds(pending.PhysicalBounds);
                                t.Bounds = item.TransformBounds(t.Bounds);
                                t.OriginalBounds = item.TransformBounds(t.OriginalBounds);
                            }
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
                            _brushes);
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
                        LegacyFilterEffectCompatibilityExecutor.ApplyShader(
                            CurrentTargets,
                            shader.Description,
                            OutputScale,
                            WorkingScale,
                            MaxWorkingScale,
                            Intent,
                            Purpose,
                            GetProgramAcquirer());
                        break;
                    }
                case FEItem_Geometry geometry:
                    {
                        Flush(false);
                        if (CurrentTargets.Count == 0) return;
                        LegacyFilterEffectCompatibilityExecutor.ApplyGeometry(
                            CurrentTargets,
                            geometry.Description,
                            OutputScale,
                            WorkingScale,
                            MaxWorkingScale,
                            Intent,
                            Purpose);
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
            _brushes);

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
        Rect physicalBounds)
    {
        public Rect InputBounds { get; set; } = inputBounds;

        public Rect PhysicalBounds { get; set; } = physicalBounds;
    }

    private readonly record struct FlushTarget(
        Rect InputBounds,
        Rect PhysicalBounds);
}
