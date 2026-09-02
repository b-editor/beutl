using System.Runtime.ExceptionServices;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

internal sealed partial class RenderRequestExecutor
{
    private sealed partial class RenderRequestExecutionState
    {
        private void ReplayOpacityMask(
            RenderFragmentReference fragment,
            ImmediateCanvas destination)
        {
            if (fragment.Inputs.Length != 1)
                throw new InvalidOperationException("An opacity mask requires exactly one input.");

            var payload = (OpacityMaskRenderFragmentPayload)fragment.Payload!;
            _ = payload.Mask.Registry.Use(
                payload.Mask,
                mask =>
                {
                    using (destination.PushOpacityMask(mask, payload.BrushBounds, payload.Invert))
                        Replay(fragment.Inputs[0], destination);
                    return true;
                });
        }

        private IReadOnlyList<MaterializedRenderValue> MaterializeOpacity(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
        {
            if (fragment.Inputs.Length != 1)
                throw new InvalidOperationException("An opacity fragment requires exactly one input.");
            if (fragment.Bounds.Width == 0 || fragment.Bounds.Height == 0)
            {
                CompleteFragmentUse(fragment.Inputs[0]);
                MarkExecutionSkipped(fragment);
                return [];
            }

            EffectiveScale scale = ClampToActiveDeviceGrid(
                fragment.Bounds,
                requestedScale ?? ResolveConcreteScale(fragment));
            RenderFragmentReference input = fragment.Inputs[0];
            IReadOnlyList<MaterializedRenderValue> values = Materialize(
                input,
                currentTarget,
                input.EffectiveScale.IsUnbounded ? scale : null);
            try
            {
                MaterializedRenderValue value = CreateOwnedValue(
                    fragment.Bounds,
                    scale,
                    allowPreviewDrop: _previewDropEligibleMaterializations.Contains(fragment));
                bool succeeded = false;
                try
                {
                    Vector rasterTranslation = DeviceGridAlignment.ResolveRasterTranslation(
                        value.DeviceBounds,
                        value.DeviceGridOffset,
                        scale.Value);
                    using var canvas = CreateExecutorCanvas(
                        value.Target,
                        scale.Value,
                        _options.MaxWorkingScale,
                        value.RasterBounds.Size,
                        _options.Intent,
                        value.DeviceBounds.Position);
                    using (canvas.PushTransform(Matrix.CreateTranslation(
                               rasterTranslation.X,
                               rasterTranslation.Y)))
                    using (canvas.PushOpacity(((OpacityRenderFragmentPayload)fragment.Payload!).Opacity))
                        DrawValues(values, canvas);
                    succeeded = true;
                    return [value];
                }
                finally
                {
                    if (!succeeded)
                        ReleaseUnpublished(value);
                }
            }
            finally
            {
                CompleteFragmentUse(input);
            }
        }

        private IReadOnlyList<MaterializedRenderValue> MaterializeOpacityMask(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
        {
            if (fragment.Inputs.Length != 1)
                throw new InvalidOperationException("An opacity mask requires exactly one input.");
            if (fragment.Bounds.Width == 0 || fragment.Bounds.Height == 0)
            {
                CompleteFragmentUse(fragment.Inputs[0]);
                MarkExecutionSkipped(fragment);
                return [];
            }

            EffectiveScale scale = ClampToActiveDeviceGrid(
                fragment.Bounds,
                requestedScale ?? ResolveConcreteScale(fragment));
            RenderFragmentReference primary = fragment.Inputs[0];
            IReadOnlyList<MaterializedRenderValue> primaryValues = Materialize(
                primary,
                currentTarget,
                primary.EffectiveScale.IsUnbounded ? scale : null);
            try
            {
                MaterializedRenderValue value = CreateOwnedValue(
                    fragment.Bounds,
                    scale,
                    allowPreviewDrop: _previewDropEligibleMaterializations.Contains(fragment));
                bool succeeded = false;
                try
                {
                    Vector rasterTranslation = DeviceGridAlignment.ResolveRasterTranslation(
                        value.DeviceBounds,
                        value.DeviceGridOffset,
                        scale.Value);
                    using var canvas = CreateExecutorCanvas(
                        value.Target,
                        scale.Value,
                        _options.MaxWorkingScale,
                        value.RasterBounds.Size,
                        _options.Intent,
                        value.DeviceBounds.Position);
                    using (canvas.PushTransform(Matrix.CreateTranslation(
                               rasterTranslation.X,
                               rasterTranslation.Y)))
                    {
                        var payload = (OpacityMaskRenderFragmentPayload)fragment.Payload!;
                        _ = payload.Mask.Registry.Use(
                            payload.Mask,
                            mask =>
                            {
                                using (canvas.PushOpacityMask(
                                           mask,
                                           payload.BrushBounds,
                                           payload.Invert))
                                {
                                    DrawValues(primaryValues, canvas);
                                }
                                return true;
                            });
                    }

                    succeeded = true;
                    return [value];
                }
                finally
                {
                    if (!succeeded)
                        ReleaseUnpublished(value);
                }
            }
            finally
            {
                CompleteFragmentUse(primary);
            }
        }

        private IReadOnlyList<MaterializedRenderValue> ExecuteOpaque(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
            => ExecuteOnDeviceGrid(
                currentTarget,
                () => ExecuteOpaqueCore(fragment, currentTarget, requestedScale));

        private IReadOnlyList<MaterializedRenderValue> ExecuteOpaqueCore(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
        {
            var payload = (OpaqueRenderFragmentPayload)fragment.Payload!;
            OpaqueRenderDescription description = payload.Description;
            var flattened = new List<MaterializedRenderValue>();
            var inputReadbacks = new List<bool>();
            var inputRanges = new List<RenderExecutionInputRange>(fragment.Inputs.Length);
            EffectiveScale outputSupply = requestedScale
                ?? (!fragment.EffectiveScale.IsUnbounded
                    ? fragment.EffectiveScale
                    : EffectiveScale.At(currentTarget.Density));
            for (int inputIndex = 0; inputIndex < fragment.Inputs.Length; inputIndex++)
            {
                RenderFragmentReference input = fragment.Inputs[inputIndex];
                IReadOnlyList<MaterializedRenderValue> inputValues = Materialize(
                    input,
                    currentTarget,
                    input.EffectiveScale.IsUnbounded ? outputSupply : null);
                RenderInputReadback readback = payload.InputReadbacks[inputIndex];
                readback.ValidateRuntimeCount(input.ValueCardinality, inputValues.Count);
                inputRanges.Add(new RenderExecutionInputRange(flattened.Count, inputValues.Count));
                for (int valueIndex = 0; valueIndex < inputValues.Count; valueIndex++)
                {
                    flattened.Add(inputValues[valueIndex]);
                    inputReadbacks.Add(readback.RequiresValue(valueIndex));
                }
            }

            try
            {
                if (payload.Topology == OpaqueRenderTopology.Map)
                {
                    var mapped = new List<MaterializedRenderValue>();
                    bool mapCallbackInvoked = false;
                    for (int inputIndex = 0; inputIndex < flattened.Count; inputIndex++)
                    {
                        MaterializedRenderValue input = flattened[inputIndex];
                        Rect outputBounds = description.Bounds.TransformBounds([input.CompleteBounds]);
                        EffectiveScale outputScale = requestedScale
                            ?? description.Scale.Resolve(
                                [input.EffectiveScale],
                                outputBounds,
                                _options.OutputScale,
                                _options.MaxWorkingScale);
                        mapped.AddRange(InvokeOpaque(
                            fragment,
                            description,
                            [input],
                            [inputReadbacks[inputIndex]],
                            [new RenderExecutionInputRange(0, 1)],
                            outputBounds,
                            outputScale,
                            description.ValueCardinality,
                            out bool currentCallbackInvoked));
                        mapCallbackInvoked |= currentCallbackInvoked;
                    }

                    if (!mapCallbackInvoked)
                        MarkExecutionSkipped(fragment);
                    return mapped;
                }

                Rect declaredBounds = description.Bounds.TransformBounds(
                    flattened.SelectToArray(static value => value.CompleteBounds));
                EffectiveScale declaredScale = requestedScale
                    ?? description.Scale.Resolve(
                        flattened.SelectToArray(static value => value.EffectiveScale),
                        declaredBounds,
                        _options.OutputScale,
                        _options.MaxWorkingScale);
                IReadOnlyList<MaterializedRenderValue> result = InvokeOpaque(
                    fragment,
                    description,
                    flattened,
                    inputReadbacks,
                    inputRanges,
                    declaredBounds,
                    declaredScale,
                    description.ValueCardinality,
                    out bool singleCallbackInvoked);
                if (!singleCallbackInvoked)
                    MarkExecutionSkipped(fragment);
                return result;
            }
            finally
            {
                foreach (RenderFragmentReference input in fragment.Inputs)
                    CompleteFragmentUse(input);
            }
        }

        private IReadOnlyList<MaterializedRenderValue> ExecuteEffectItem(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget)
            => ExecuteOnDeviceGrid(
                currentTarget,
                () => ExecuteEffectItemCore(fragment, currentTarget),
                normalizeGridPhase: fragment.Payload is FilterEffectSegmentRenderFragmentPayload payload
                                    && payload.HasImperativeItem);

        private IReadOnlyList<MaterializedRenderValue> ExecuteEffectItemCore(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget)
        {
            Rect requiredRegion = ResolveFragmentRequirement(fragment, fragment.Bounds);
            var payload = (FilterEffectSegmentRenderFragmentPayload)fragment.Payload!;
            if (FilterEffectSegmentDirectReplaySupport.CanMaterialize(fragment))
            {
                return ExecuteDirectSkiaFilterMaterialization(
                    fragment,
                    currentTarget,
                    payload,
                    requiredRegion);
            }

            var inputs = new List<MaterializedRenderValue>();
            EffectiveScale inputRequestScale = fragment.EffectiveScale.IsUnbounded
                ? EffectiveScale.At(currentTarget.Density)
                : fragment.EffectiveScale;
            for (int index = 0; index < fragment.Inputs.Length; index++)
            {
                RenderFragmentReference input = fragment.Inputs[index];
                inputs.AddRange(Materialize(
                    input,
                    currentTarget,
                    input.EffectiveScale.IsUnbounded ? inputRequestScale : null));
            }

            try
            {
                return payload.Context.Registry.Use(
                    payload.Context,
                    effectContext =>
                    {
                        using var targets = new EffectTargets();
                        foreach (MaterializedRenderValue input in inputs)
                        {
                            bool hasCompleteBacking = input.RasterBounds.Contains(input.CompleteBounds);
                            Rect inputBounds = hasCompleteBacking ? input.CompleteBounds : input.Bounds;
                            targets.Add(new EffectTarget(
                                input.Target,
                                inputBounds,
                                input.EffectiveScale,
                                input.DeviceBounds,
                                input.DeviceGridOffset,
                                input.PreserveImperativeRasterPlacement && hasCompleteBacking)
                            {
                                OriginalBounds = new Rect(default, inputBounds.Size),
                                Bounds = inputBounds,
                            });
                        }

                        using var builder = new SKImageFilterBuilder();
                        using var activator = new FilterEffectActivator(
                            targets,
                            builder,
                            _options.Intent,
                            _options.Purpose,
                            _options.OutputScale,
                            fragment.EffectiveScale.Value,
                            _options.MaxWorkingScale,
                            _activeDeviceGridOffset,
                            (target, source) => AcquireStandaloneProgram(
                                target,
                                source),
                            _drawableBrushMaterializer,
                            useExecutorManagedCanvas: true,
                            renderTargetLeaseSession: _targets,
                            targetDomain: _options.TargetDomain);
                        activator.Apply(effectContext);
                        activator.CompletePolicyBoundary(
                            payload.WorkingScalePolicy.HasValue);

                        var result = new List<MaterializedRenderValue>(activator.CurrentTargets.Count);
                        foreach (EffectTarget target in activator.CurrentTargets)
                        {
                            if (target.RenderTarget is not { } renderTarget)
                                continue;

                            MaterializedRenderValue value = MaterializeEffectItemTarget(
                                target,
                                renderTarget,
                                target.Bounds);
                            _ownedValues.Add(value);

                            // Cropping the input to the backward region leaves the surrounding output
                            // undefined, so the published value must not claim it.
                            Rect selectedBounds = value.Bounds.Intersect(requiredRegion);
                            if (selectedBounds.Width == 0 || selectedBounds.Height == 0)
                            {
                                ReleaseUnpublished(value);
                                continue;
                            }

                            if (selectedBounds != value.Bounds)
                            {
                                if (value.PreserveImperativeRasterPlacement
                                    || value.RasterBounds.Contains(value.CompleteBounds))
                                {
                                    // Preserve a complete backing so later effect-item effects can sample
                                    // the physical footprint while Bounds remains the selected output.
                                    value.Bounds = selectedBounds;
                                }
                                else
                                {
                                    MaterializedRenderValue cropped = CropValue(
                                        fragment,
                                        value,
                                        selectedBounds);
                                    ReleaseUnpublished(value);
                                    value = cropped;
                                }
                            }

                            result.Add(value);
                        }

                        return (IReadOnlyList<MaterializedRenderValue>)result;
                    });
            }
            finally
            {
                foreach (RenderFragmentReference input in fragment.Inputs)
                    CompleteFragmentUse(input);
            }
        }

        private IReadOnlyList<MaterializedRenderValue> ExecuteDirectSkiaFilterMaterialization(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            FilterEffectSegmentRenderFragmentPayload payload,
            Rect requiredRegion)
        {
            RenderFragmentReference input = fragment.Inputs[0];
            if (requiredRegion.Width == 0 || requiredRegion.Height == 0)
            {
                CompleteFragmentUse(input);
                MarkExecutionSkipped(fragment);
                return [];
            }

            float requestedDensity = fragment.EffectiveScale.IsUnbounded
                ? currentTarget.Density
                : fragment.EffectiveScale.Value;
            EffectiveScale scale = ClampToActiveDeviceGrid(
                fragment.Bounds,
                EffectiveScale.At(requestedDensity));
            MaterializedRenderValue? output = null;
            bool succeeded = false;
            bool replayStarted = false;
            try
            {
                output = CreateOwnedValue(
                    requiredRegion,
                    scale,
                    fragment.Bounds,
                    allowPreviewDrop: _previewDropEligibleMaterializations.Contains(fragment));
                using var builder = new SKImageFilterBuilder();
                foreach (IFEItem item in payload.BoundsItems)
                    ((IFEItem_Skia)item).AcceptsDirect(builder);

                using var paint = builder.HasFilter()
                    ? new SKPaint { ImageFilter = builder.GetFilter() }
                    : null;
                Vector rasterTranslation = DeviceGridAlignment.ResolveRasterTranslation(
                    output.DeviceBounds,
                    output.DeviceGridOffset,
                    scale.Value);
                using var canvas = CreateExecutorCanvas(
                    output.Target,
                    scale.Value,
                    _options.MaxWorkingScale,
                    output.RasterBounds.Size,
                    _options.Intent,
                    output.DeviceBounds.Position);
                using (canvas.PushTransform(Matrix.CreateTranslation(
                           rasterTranslation.X,
                           rasterTranslation.Y)))
                {
                    if (paint is not null)
                    {
                        Rect replayedInputBounds = ResolveFragmentRequirement(input, input.Bounds);
                        Rect layerContentBounds = GetDirectFilterLayerBounds(
                            input.Bounds,
                            replayedInputBounds);
                        using (canvas.PushBlendMode(BlendMode.SrcOver))
                        using (canvas.PushTransform(Matrix.Identity))
                        // The filter layer must match the region Replay writes, not the input's full
                        // semantic bounds. A wider layer exposes unwritten pixels to spatial filters.
                        using (canvas.PushFilterLayer(paint, layerContentBounds))
                        {
                            replayStarted = true;
                            Replay(input, canvas);
                        }
                    }
                    else
                    {
                        replayStarted = true;
                        Replay(input, canvas);
                    }
                }

                succeeded = true;
                return [output];
            }
            finally
            {
                if (!replayStarted)
                    CompleteFragmentUse(input);
                if (!succeeded && output is not null)
                    ReleaseUnpublished(output);
            }
        }

        private MaterializedRenderValue MaterializeEffectItemTarget(
            EffectTarget target,
            RenderTarget renderTarget,
            Rect completeBounds)
        {
            if (target.PreserveImperativeRasterPlacement)
            {
                Vector deviceGridOffset = target.DeviceBounds
                    .ToRect(target.Scale.Value)
                    .Position - target.RasterBounds.Position;
                return CreateOwnedEffectItemValue(
                    target,
                    renderTarget,
                    target.Bounds,
                    target.Scale,
                    target.DeviceBounds,
                    deviceGridOffset,
                    completeBounds,
                    preserveImperativeRasterPlacement: true);
            }

            Rect canonicalRasterBounds = target.DeviceBounds
                .ToRect(target.Scale.Value)
                .Translate(-target.DeviceGridOffset);
            PixelRect semanticDeviceBounds = PixelRect.FromRect(
                target.Bounds.Translate(target.DeviceGridOffset),
                target.Scale.Value);
            if (target.RasterBounds == canonicalRasterBounds
                && target.DeviceBounds.Contains(semanticDeviceBounds))
            {
                return CreateOwnedEffectItemValue(
                    target,
                    renderTarget,
                    target.Bounds,
                    target.Scale,
                    target.DeviceBounds,
                    target.DeviceGridOffset,
                    completeBounds: completeBounds);
            }

            Rect physicalBounds = target.RasterBounds.Union(target.Bounds);
            float density = RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(
                physicalBounds.Translate(target.DeviceGridOffset),
                target.Scale.Value);
            EffectiveScale normalizedScale = EffectiveScale.At(density);
            PixelRect normalizedDeviceBounds = PixelRect.FromRect(physicalBounds, density);
            MaterializedRenderValue normalized = CreateOwnedValue(
                target.Bounds,
                normalizedScale,
                completeBounds,
                physicalDeviceBounds: normalizedDeviceBounds,
                deviceGridOffset: target.DeviceGridOffset,
                allowPreviewDrop: true);
            bool succeeded = false;
            try
            {
                Vector rasterTranslation = DeviceGridAlignment.ResolveRasterTranslation(
                    normalized.DeviceBounds,
                    normalized.DeviceGridOffset,
                    normalized.EffectiveScale.Value);
                using var canvas = CreateExecutorCanvas(
                    normalized.Target,
                    normalized.EffectiveScale.Value,
                    _options.MaxWorkingScale,
                    normalized.RasterBounds.Size,
                    _options.Intent,
                    normalized.DeviceBounds.Position);
                using (canvas.PushTransform(Matrix.CreateTranslation(
                           rasterTranslation.X,
                           rasterTranslation.Y)))
                {
                    canvas.DrawRenderTargetScaledWithoutFlush(renderTarget, target.RasterBounds);
                }

                succeeded = true;
                return normalized;
            }
            finally
            {
                if (!succeeded)
                    ReleaseUnpublished(normalized);
            }
        }

        private static MaterializedRenderValue CreateOwnedEffectItemValue(
            EffectTarget effectTarget,
            RenderTarget renderTarget,
            Rect bounds,
            EffectiveScale effectiveScale,
            PixelRect deviceBounds,
            Vector deviceGridOffset,
            Rect? completeBounds = null,
            bool preserveImperativeRasterPlacement = false)
        {
            EffectTargetRenderTargetLease? renderTargetLease = effectTarget.TakeRenderTargetLease();
            if (renderTargetLease is null)
            {
                return CreateOwnedShallowCopy(
                    renderTarget,
                    bounds,
                    effectiveScale,
                    deviceBounds,
                    deviceGridOffset,
                    completeBounds,
                    preserveImperativeRasterPlacement);
            }

            try
            {
                return new MaterializedRenderValue(
                    renderTargetLease,
                    bounds,
                    effectiveScale,
                    deviceBounds,
                    deviceGridOffset,
                    completeBounds,
                    preserveImperativeRasterPlacement);
            }
            catch
            {
                renderTargetLease.Dispose();
                throw;
            }
        }

    }
}
