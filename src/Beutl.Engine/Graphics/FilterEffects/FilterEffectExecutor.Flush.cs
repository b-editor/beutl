using Beutl.Graphics.Rendering;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed partial class FilterEffectExecutor
{
    /// <summary>The widest target list a flush plan is held on the stack for.</summary>
    /// <remarks>
    /// A flush almost always sees one target - a split effect is what produces more, and the widest any
    /// built-in reaches across the graphics suite is nine. Past this width the plan is heap-allocated,
    /// which costs one array where the map this replaced cost three objects at every width.
    /// </remarks>
    private const int StackFlushPlanLimit = 16;

    public void Flush(bool force = true)
    {
        EnsureLinearTargets();
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
        BufferDimensionBudget budget = Budget;
        float fit = imperativeSegmentBoundary
            ? budget.ClampWorkingScale(budgetBounds, WorkingScale)
            : budget.ClampWorkingScaleToExactFootprint(
                budgetBounds.Translate(target.DeviceGridOffset),
                WorkingScale);
        if (fit >= WorkingScale)
            return;

        s_logger.LogWarning(
            "Working scale clamped {From} -> {To} to keep an effect buffer within the {Limit} px GPU axis limit (bounds {Bounds}).",
            WorkingScale, fit, budget.MaxDimension, budgetBounds);
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

    private void ThrowIfDeliveryAllocationFailure(string message)
    {
        if (Intent == RenderIntent.Delivery)
        {
            throw new InvalidOperationException(message);
        }
    }

    private readonly record struct FlushTarget(
        Rect InputBounds,
        Rect PhysicalBounds);
}
