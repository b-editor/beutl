using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed class FilterEffectActivator : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger("FilterEffectActivator");
    private Dictionary<EffectTarget, PendingSkiaTarget>? _pendingSkiaTargets;

    public FilterEffectActivator(
        EffectTargets targets,
        SKImageFilterBuilder builder,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        float outputScale = 1f,
        float workingScale = 1f,
        float maxWorkingScale = float.PositiveInfinity)
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

    // Canonical ceiling rule, plus a warning when it substitutes.
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

    public void Dispose()
    {
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
            float fit = RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(
                flushTarget.PhysicalBounds, WorkingScale);
            if (fit < WorkingScale)
            {
                s_logger.LogWarning(
                    "Working scale clamped {From} -> {To} to keep an effect buffer within the GPU axis limit (bounds {Bounds}).",
                    WorkingScale, fit, flushTarget.PhysicalBounds);
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
            if (!hasFilter && CanReuseWithoutFilter(target, w))
                continue;

            PixelRect allocationDeviceBounds = CustomFilterEffectContext.DeviceBufferBounds(
                flushTarget.PhysicalBounds, w);
            PixelRect deviceBounds = hasFilter
                ? PublishFilteredDeviceBounds(target, allocationDeviceBounds, w)
                : allocationDeviceBounds;
            Rect rasterBounds = deviceBounds.ToRect(w);
            using RenderTarget? surface = RenderTarget.Create(
                allocationDeviceBounds.Width,
                allocationDeviceBounds.Height);

            if (surface != null)
            {
                using (var canvas = new ImmediateCanvas(surface, w, MaxWorkingScale,
                           logicalSize: rasterBounds.Size))
                {
                    canvas.Clear();
                    using (canvas.PushTransform(
                               Matrix.CreateTranslation(
                                   flushTarget.InputBounds.X - rasterBounds.X,
                                   flushTarget.InputBounds.Y - rasterBounds.Y)))
                    using (paint != null ? canvas.PushPaint(paint) : default)
                    {
                        target.Draw(canvas);
                    }
                }

                var newTarget = new EffectTarget(
                    surface,
                    target.Bounds,
                    EffectiveScale.At(w),
                    deviceBounds)
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

    private FlushTarget ResolveFlushTarget(EffectTarget target, bool hasFilter)
    {
        if (!hasFilter)
        {
            return new FlushTarget(
                target.Bounds,
                target.RasterBounds.Union(target.Bounds));
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

    private static PixelRect PublishFilteredDeviceBounds(
        EffectTarget target,
        PixelRect localDeviceBounds,
        float density)
    {
        PixelRect semanticDeviceBounds = PixelRect.FromRect(target.Bounds, density);
        Vector localToGlobalOffset = target.Bounds.Position - target.OriginalBounds.Position;
        PixelPoint publishedOrigin = localDeviceBounds.Position + new PixelPoint(
            (int)MathF.Floor(localToGlobalOffset.X * density),
            (int)MathF.Floor(localToGlobalOffset.Y * density));
        var result = new PixelRect(publishedOrigin, localDeviceBounds.Size);
        if (!Contains(result, semanticDeviceBounds))
        {
            throw new InvalidOperationException(
                "A filtered physical footprint must contain its semantic device bounds.");
        }

        return result;
    }

    private static bool CanReuseWithoutFilter(EffectTarget target, float density)
    {
        if (target.Scale.IsUnbounded || target.Scale.Value != density)
            return false;

        PixelRect semanticDeviceBounds = PixelRect.FromRect(target.Bounds, density);
        return target.RasterBounds == target.DeviceBounds.ToRect(density)
               && Contains(target.DeviceBounds, semanticDeviceBounds)
               && target.DeviceBounds.Width <= RenderScaleUtilities.MaxBufferDimension
               && target.DeviceBounds.Height <= RenderScaleUtilities.MaxBufferDimension;
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
        if (CurrentTargets.Count == 0) return;

        foreach (IFEItem item in context._items)
        {
            switch (item)
            {
                case IFEItem_Skia skia:
                    {
                        BeginSkiaChain();
                        skia.Accepts(this, Builder);
                        foreach (EffectTarget t in CurrentTargets)
                        {
                            PendingSkiaTarget pending = _pendingSkiaTargets![t];
                            pending.PhysicalBounds = item.TransformBounds(pending.PhysicalBounds);
                            t.Bounds = item.TransformBounds(t.Bounds);
                            t.OriginalBounds = item.TransformBounds(t.OriginalBounds);
                        }

                        break;
                    }
                case IFEItem_Custom custom:
                    {
                        Flush();
                        if (CurrentTargets.Count == 0) return;

                        var customContext = new CustomFilterEffectContext(
                            CurrentTargets,
                            Intent,
                            Purpose,
                            OutputScale,
                            WorkingScale,
                            MaxWorkingScale);
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
                            Purpose);
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
        Flush(false);

        using EffectTargets cloned = CurrentTargets.Clone();
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            cloned,
            builder,
            Intent,
            Purpose,
            OutputScale,
            WorkingScale,
            MaxWorkingScale);

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
        public Rect InputBounds { get; } = inputBounds;

        public Rect PhysicalBounds { get; set; } = physicalBounds;
    }

    private readonly record struct FlushTarget(
        Rect InputBounds,
        Rect PhysicalBounds);
}
