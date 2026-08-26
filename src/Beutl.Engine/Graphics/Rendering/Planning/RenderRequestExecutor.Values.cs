using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

internal sealed partial class RenderRequestExecutor
{
    private sealed partial class RenderRequestExecutionState
    {
        private void AddValueReferences(IEnumerable<MaterializedRenderValue> values)
        {
            foreach (MaterializedRenderValue value in values)
            {
                _valueReferences.TryGetValue(value, out int references);
                _valueReferences[value] = checked(references + 1);
            }
        }

        private void ReleaseValueReference(MaterializedRenderValue value)
        {
            if (!_valueReferences.TryGetValue(value, out int references) || references <= 0)
                throw new InvalidOperationException("A render value reference was released more than once.");

            if (references > 1)
            {
                _valueReferences[value] = references - 1;
                return;
            }

            _valueReferences.Remove(value);
            if (!_cacheCaptureValues.Contains(value))
                ReleaseUnpublished(value);
        }

        private void CompleteFragmentUse(RenderFragmentReference fragment)
        {
            if (!_resourceUses.CompleteUse(fragment))
                return;
            if (!_values.Remove(fragment, out IReadOnlyList<MaterializedRenderValue>? values))
                return;

            foreach (MaterializedRenderValue value in values)
                ReleaseValueReference(value);
        }

        private void MarkExecutionSkipped(RenderFragmentReference fragment)
        {
            if (fragment.Id is { } id)
                _skippedExecutionSubjects.Add(id);
        }

        private static void AddResolvedDomain(
            Dictionary<RenderFragmentId, Rect> domains,
            RenderFragmentId fragmentId,
            Rect domain)
        {
            if (domains.TryGetValue(fragmentId, out Rect existing) && existing != domain)
            {
                throw new InvalidOperationException(
                    "One target-effect fragment cannot execute in two different target domains.");
            }

            domains[fragmentId] = domain;
        }

        private T ExecuteOnDeviceGrid<T>(
            ImmediateCanvas currentTarget,
            Func<T> execute,
            bool normalizeGridPhase = false)
        {
            Vector previousOffset = _activeDeviceGridOffset;
            bool previousNormalized = _deviceGridPhaseNormalized;
            // A custom effect's buffers must start on the pixel its logical origin names, because the
            // effect does its own device-pixel arithmetic against them. A grid whose phase is
            // fractional cannot deliver that, so the flush would resample the input onto the phase
            // instead, and half a pixel of edge coverage is lost before the effect ever sees it.
            // The requirement is inherited: the input is rasterized in whatever frame materializes it,
            // which for a chained segment is a nested frame that re-derives the grid from this canvas.
            bool normalized = previousNormalized || normalizeGridPhase;
            Vector offset = DeviceGridAlignment.ResolveLogicalOffset(currentTarget);
            if (normalized)
                offset -= DeviceGridAlignment.NormalizePhase(offset, currentTarget.Density);
            _activeDeviceGridOffset = offset;
            _deviceGridPhaseNormalized = normalized;
            try
            {
                return execute();
            }
            finally
            {
                _activeDeviceGridOffset = previousOffset;
                _deviceGridPhaseNormalized = previousNormalized;
            }
        }

        private MaterializedRenderValue CreateOwnedValue(
            Rect bounds,
            EffectiveScale scale,
            Rect? completeBounds = null,
            PixelRect? physicalDeviceBounds = null,
            Vector? deviceGridOffset = null,
            bool physicalDeviceBoundsAreAligned = false,
            bool allowPreviewDrop = false,
            bool initializeTarget = true)
        {
            if (scale.IsUnbounded)
                throw new InvalidOperationException("An allocated render value requires a concrete density.");
            Vector gridOffset = deviceGridOffset ?? _activeDeviceGridOffset;
            // The density stays the plan's here even where the device attaches less than the engine ceiling
            // the plan was clamped to. The render cache keys an entry on the planned materialization density
            // and refuses a payload recorded at any other, so re-clamping to the device would turn every
            // cacheable fragment on such a device into a failed capture. An over-budget footprint is instead
            // declined by the pool, which reaches the caller as the preview drop or the delivery failure the
            // lease session already defines.
            PixelRect semanticDeviceBounds = PixelRect.FromRect(
                bounds.Translate(gridOffset),
                scale.Value);
            PixelRect deviceBounds;
            if (physicalDeviceBounds is not { } requestedPhysicalBounds)
            {
                deviceBounds = semanticDeviceBounds;
            }
            else if (physicalDeviceBoundsAreAligned || gridOffset == default)
            {
                deviceBounds = requestedPhysicalBounds;
            }
            else
            {
                PixelRect localSemanticBounds = PixelRect.FromRect(bounds, scale.Value);
                int leftApron = localSemanticBounds.X - requestedPhysicalBounds.X;
                int topApron = localSemanticBounds.Y - requestedPhysicalBounds.Y;
                int rightApron = requestedPhysicalBounds.Right - localSemanticBounds.Right;
                int bottomApron = requestedPhysicalBounds.Bottom - localSemanticBounds.Bottom;
                deviceBounds = new PixelRect(
                    semanticDeviceBounds.X - leftApron,
                    semanticDeviceBounds.Y - topApron,
                    semanticDeviceBounds.Width + leftApron + rightApron,
                    semanticDeviceBounds.Height + topApron + bottomApron);
            }
            if (deviceBounds.Width <= 0
                || deviceBounds.Height <= 0
                || deviceBounds.X > semanticDeviceBounds.X
                || deviceBounds.Y > semanticDeviceBounds.Y
                || deviceBounds.Right < semanticDeviceBounds.Right
                || deviceBounds.Bottom < semanticDeviceBounds.Bottom)
            {
                throw new ArgumentException(
                    "An allocated render value's physical device bounds must contain its semantic bounds.",
                    nameof(physicalDeviceBounds));
            }
            // allowPreviewDrop states whether this materialization may be given up under allocation
            // pressure. A device-budget refusal is not pressure - no allocator this session can reach will
            // ever attach the buffer - so the render intent decides it instead: the session drops the
            // contribution under Preview and still fails naming the limit under Delivery.
            RenderTargetLease? lease = allowPreviewDrop || _targets.ExceedsBufferBudget(deviceBounds.Size)
                ? _targets.TryAcquire(deviceBounds.Size)
                : _targets.Acquire(deviceBounds.Size);
            if (lease is null)
                throw new PreviewAllocationDropException();
            _intermediateTargetAcquisitions++;
            bool succeeded = false;
            try
            {
                if (initializeTarget)
                {
                    if (!lease.Target.HasTransparentContents)
                        lease.Target.ClearToTransparent();
                }
                var value = new MaterializedRenderValue(
                    lease,
                    bounds,
                    scale,
                    deviceBounds,
                    gridOffset,
                    completeBounds);
                _ownedValues.Add(value);
                succeeded = true;
                return value;
            }
            finally
            {
                if (!succeeded)
                    lease.Dispose();
            }
        }

        private void ReleaseUnpublished(MaterializedRenderValue value)
        {
            if (_ownedValues.Remove(value))
                DisposeOwnedValue(value);
        }

        private void DisposeOwnedValue(MaterializedRenderValue value)
        {
            value.Dispose();
        }

        private EffectiveScale ResolveConcreteScale(RenderFragmentReference fragment)
        {
            float scale = fragment.EffectiveScale.IsUnbounded
                ? RenderScaleUtilities.ResolveWorkingScale(
                    fragment.Inputs.Select(static input => input.EffectiveScale).ToArray(),
                    _options.OutputScale,
                    _options.MaxWorkingScale)
                : fragment.EffectiveScale.Value;
            scale = RenderMaterializationDensityPolicy.Clamp(fragment, scale);
            return EffectiveScale.At(scale);
        }

        private EffectiveScale ClampToActiveDeviceGrid(
            Rect completeBounds,
            EffectiveScale scale,
            bool requiresRasterApron = false)
        {
            return ClampToDeviceGrid(
                completeBounds,
                scale,
                _activeDeviceGridOffset,
                requiresRasterApron);
        }

        private EffectiveScale ClampToDeviceGrid(
            Rect completeBounds,
            EffectiveScale scale,
            Vector deviceGridOffset,
            bool requiresRasterApron = false)
        {
            Rect alignedBounds = completeBounds.Translate(deviceGridOffset);
            float density = requiresRasterApron
                ? RenderScaleUtilities.ClampWorkingScaleToRasterApronBudget(
                    alignedBounds,
                    scale.Value)
                : RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(
                    alignedBounds,
                    scale.Value);
            return EffectiveScale.At(density);
        }

        private static void DrawValues(
            IReadOnlyList<MaterializedRenderValue> values,
            ImmediateCanvas destination)
        {
            foreach (MaterializedRenderValue value in values)
                DrawValue(value, destination);
        }

        private static void DrawValue(
            MaterializedRenderValue value,
            ImmediateCanvas destination)
        {
            if (value.PreserveImperativeRasterPlacement
                && value.EffectiveScale.Value == 1f
                && destination.Density == 1f)
            {
                destination.DrawRenderTarget(value.Target, value.RasterBounds.Position);
            }
            else
            {
                destination.DrawRenderTargetScaledWithoutFlush(value.Target, value.RasterBounds);
            }
        }

        private static void ValidateOutputCount(
            RenderValueCardinality cardinality,
            int count)
        {
            if (count < cardinality.Minimum
                || (cardinality.Maximum is { } maximum && count > maximum))
            {
                throw new InvalidOperationException(
                    $"The deferred callback published {count} values outside its declared cardinality "
                    + $"[{cardinality.Minimum}, {cardinality.Maximum?.ToString() ?? "unbounded"}].");
            }
        }

        private static MaterializedRenderValue CreateOwnedShallowCopy(
            RenderTarget target,
            Rect bounds,
            EffectiveScale effectiveScale,
            PixelRect deviceBounds,
            Vector deviceGridOffset = default,
            Rect? completeBounds = null,
            bool preserveImperativeRasterPlacement = false)
        {
            RenderTarget copy = target.ShallowCopy();
            try
            {
                return new MaterializedRenderValue(
                    copy,
                    bounds,
                    effectiveScale,
                    deviceBounds,
                    ownsTarget: true,
                    deviceGridOffset: deviceGridOffset,
                    completeBounds: completeBounds,
                    preserveImperativeRasterPlacement: preserveImperativeRasterPlacement);
            }
            catch
            {
                copy.Dispose();
                throw;
            }
        }

    }
}
