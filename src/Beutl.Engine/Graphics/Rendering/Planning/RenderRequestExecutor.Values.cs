using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

internal sealed partial class RenderRequestExecutor
{
    private sealed partial class CompatibilityExecutionState
    {
        private void AddValueReferences(IEnumerable<CompatibilityRenderValue> values)
        {
            foreach (CompatibilityRenderValue value in values)
            {
                _valueReferences.TryGetValue(value, out int references);
                _valueReferences[value] = checked(references + 1);
            }
        }

        private void ReleaseValueReference(CompatibilityRenderValue value)
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
            if (!_values.Remove(fragment, out IReadOnlyList<CompatibilityRenderValue>? values))
                return;

            foreach (CompatibilityRenderValue value in values)
                ReleaseValueReference(value);
        }

        private void MarkExecutionSkipped(RenderFragmentReference fragment)
        {
            if (fragment.Id is { } id)
                _skippedExecutionSubjects.Add(id);
        }

        private IDisposable? ObserveGpuPass(RenderFragmentReference fragment)
        {
            if (_diagnostics is not { } diagnostics)
                return null;

            long subjectId = fragment.Id?.Value ?? 0;
            return ImmediateCanvas.ObservePixelOperations(
                () => diagnostics.RecordGpuPassExecuted(subjectId));
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

        private T ExecuteOnDeviceGrid<T>(ImmediateCanvas currentTarget, Func<T> execute)
        {
            Vector previous = _activeDeviceGridOffset;
            _activeDeviceGridOffset = DeviceGridAlignment.ResolveLogicalOffset(currentTarget);
            try
            {
                return execute();
            }
            finally
            {
                _activeDeviceGridOffset = previous;
            }
        }

        private CompatibilityRenderValue CreateOwnedValue(
            Rect bounds,
            EffectiveScale scale,
            Rect? completeBounds = null,
            PixelRect? physicalDeviceBounds = null,
            Vector? deviceGridOffset = null,
            bool physicalDeviceBoundsAreAligned = false,
            bool allowPreviewDrop = false)
        {
            if (scale.IsUnbounded)
                throw new InvalidOperationException("An allocated render value requires a concrete density.");
            Vector gridOffset = deviceGridOffset ?? _activeDeviceGridOffset;
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
            RenderTargetLease? lease;
            RenderTargetPoolStatistics beforeAcquire = _targets.PoolStatistics;
            try
            {
                lease = allowPreviewDrop
                    ? _targets.TryAcquire(deviceBounds.Size)
                    : _targets.Acquire(deviceBounds.Size);
            }
            catch
            {
                RenderTargetPoolStatistics afterFailure = _targets.PoolStatistics;
                _diagnostics?.RecordPoolMissWithoutAcquisition(
                    afterFailure.Misses - beforeAcquire.Misses);
                RecordFailure(RenderPipelineFailurePhase.Allocation, ActiveSubjectId);
                throw;
            }
            if (lease is null)
            {
                RenderTargetPoolStatistics afterFailure = _targets.PoolStatistics;
                _diagnostics?.RecordPoolMissWithoutAcquisition(
                    afterFailure.Misses - beforeAcquire.Misses);
                _diagnostics?.RecordPreviewAllocationDrop();
                throw new PreviewAllocationDropException();
            }
            _intermediateTargetAcquisitions++;
            _diagnostics?.RecordIntermediateAcquired(
                created: !lease.WasReused,
                poolHit: lease.WasReused);
            _diagnostics?.RecordMaterialization(fullFrame: _options.RequestedRegion is null);
            bool succeeded = false;
            try
            {
                lease.Target.Value.Canvas.Clear(SKColors.Transparent);
                var value = new CompatibilityRenderValue(
                    lease,
                    bounds,
                    scale,
                    deviceBounds,
                    gridOffset,
                    completeBounds);
                _ownedValues.Add(value);
                _diagnosticIntermediates.Add(value);
                succeeded = true;
                return value;
            }
            catch
            {
                RecordFailure(RenderPipelineFailurePhase.Allocation, ActiveSubjectId);
                throw;
            }
            finally
            {
                if (!succeeded)
                {
                    lease.Dispose();
                    _diagnostics?.RecordIntermediateDischarged();
                }
            }
        }

        private void ReleaseUnpublished(CompatibilityRenderValue value)
        {
            if (_ownedValues.Remove(value))
                DisposeOwnedValue(value);
        }

        private void DisposeOwnedValue(CompatibilityRenderValue value)
        {
            try
            {
                value.Dispose();
            }
            finally
            {
                if (_diagnosticIntermediates.Remove(value))
                    _diagnostics?.RecordIntermediateDischarged();
            }
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

        private static EffectiveScale ClampToDeviceGrid(
            Rect completeBounds,
            EffectiveScale scale,
            Vector deviceGridOffset,
            bool requiresRasterApron = false)
        {
            if (deviceGridOffset == default)
                return scale;

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
            IReadOnlyList<CompatibilityRenderValue> values,
            ImmediateCanvas destination)
        {
            foreach (CompatibilityRenderValue value in values)
                DrawValue(value, destination);
        }

        private static void DrawValue(
            CompatibilityRenderValue value,
            ImmediateCanvas destination)
        {
            if (value.PreserveLegacyRasterPlacement
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

        private static CompatibilityRenderValue CreateOwnedShallowCopy(
            RenderTarget target,
            Rect bounds,
            EffectiveScale effectiveScale,
            PixelRect deviceBounds,
            Vector deviceGridOffset = default,
            Rect? completeBounds = null,
            bool preserveLegacyRasterPlacement = false)
        {
            RenderTarget copy = target.ShallowCopy();
            try
            {
                return new CompatibilityRenderValue(
                    copy,
                    bounds,
                    effectiveScale,
                    deviceBounds,
                    ownsTarget: true,
                    deviceGridOffset: deviceGridOffset,
                    completeBounds: completeBounds,
                    preserveLegacyRasterPlacement: preserveLegacyRasterPlacement);
            }
            catch
            {
                copy.Dispose();
                throw;
            }
        }

    }
}
