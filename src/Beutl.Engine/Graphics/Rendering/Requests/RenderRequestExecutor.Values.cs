using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.Graphics.Rendering.Requests;

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
            if (!IsCacheCaptureValue(value))
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

        private static void AddResolvedDomain(
            ref Dictionary<RenderFragmentId, Rect>? domains,
            RenderFragmentId fragmentId,
            Rect domain)
        {
            domains ??= [];
            if (domains.TryGetValue(fragmentId, out Rect existing) && existing != domain)
            {
                throw new InvalidOperationException(
                    "One target-effect fragment cannot execute in two different target domains.");
            }

            domains[fragmentId] = domain;
        }

        private static TargetScopePlan GetScope(
            ImmutableArray<TargetScopePlan> scopes,
            TargetScopeId id)
        {
            int index = id.Value - 1;
            if ((uint)index >= (uint)scopes.Length || scopes[index].Id != id)
                throw new InvalidOperationException("A target dependency references a non-canonical scope ID.");
            return scopes[index];
        }

        private bool IsCacheCaptureValue(MaterializedRenderValue value)
            => _cacheCaptureValues?.Contains(value) == true;

        private void AddCacheCaptureValue(MaterializedRenderValue value)
            => (_cacheCaptureValues ??= new(ReferenceEqualityComparer.Instance)).Add(value);

        private void RemoveCacheCaptureValue(MaterializedRenderValue value)
            => _cacheCaptureValues?.Remove(value);

        private void ClearCacheCaptureValues()
            => _cacheCaptureValues = null;

        private T ExecuteOnDeviceGrid<T>(
            ImmediateCanvas currentTarget,
            Func<T> execute,
            bool normalizeGridPhase = false)
        {
            Vector previousOffset = _activeDeviceGridOffset;
            bool previousNormalized = _deviceGridPhaseNormalized;
            // Custom effects require logical origins on exact buffer pixels; fractional grid phases resample
            // and lose edge coverage. Nested materialization inherits this normalization.
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
            // Cache identity requires the planned density. The pool reports device-budget failures through
            // the existing preview-drop or delivery-failure path instead of re-clamping it.
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
                    fragment.Inputs.SelectToArray(static input => input.EffectiveScale),
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
