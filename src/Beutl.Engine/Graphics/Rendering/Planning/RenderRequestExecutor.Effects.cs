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
        private void ReplayOpacityMask(
            RenderFragmentReference fragment,
            ImmediateCanvas destination)
        {
            if (fragment.Inputs.Length == 0)
                throw new InvalidOperationException("An opacity mask requires a primary input.");

            var payload = (OpacityMaskRenderFragmentPayload)fragment.Payload!;
            var values = new List<CompatibilityRenderValue>();
            for (int index = 1; index < fragment.Inputs.Length; index++)
            {
                values.AddRange(Materialize(
                    fragment.Inputs[index],
                    destination,
                    EffectiveScale.At(destination.Density)));
            }

            var images = new List<SKImage>();
            var token = new RenderExecutionSessionToken();
            try
            {
                token.RunAndComplete(
                    () =>
                    {
                        IReadOnlyList<RenderExecutionInput> inputs = CreateExecutionInputs(
                            token,
                            values,
                            requiresReadback: false,
                            readbackOwner: null,
                            images);
                        BrushExecutionResolver.UseBrush(
                            token,
                            payload.Resources,
                            inputs,
                            payload.Mask,
                            mask =>
                            {
                                using (ObserveGpuPass(fragment))
                                using (destination.PushOpacityMask(mask, payload.BrushBounds, payload.Invert))
                                    Replay(fragment.Inputs[0], destination);
                            });
                    });
            }
            finally
            {
                foreach (SKImage image in images)
                    image.Dispose();
                for (int index = 1; index < fragment.Inputs.Length; index++)
                    CompleteFragmentUse(fragment.Inputs[index]);
            }
        }

        private IReadOnlyList<CompatibilityRenderValue> MaterializeOpacity(
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
            IReadOnlyList<CompatibilityRenderValue> values = Materialize(
                input,
                currentTarget,
                input.EffectiveScale.IsUnbounded ? scale : null);
            try
            {
                CompatibilityRenderValue value = CreateOwnedValue(
                    fragment.Bounds,
                    scale,
                    allowPreviewDrop: _previewDropEligibleMaterializations.Contains(fragment));
                _diagnostics?.RecordGpuPassExecuted(fragment.Id?.Value ?? 0);
                bool succeeded = false;
                try
                {
                    Vector rasterTranslation = DeviceGridAlignment.ResolveRasterTranslation(
                        value.DeviceBounds,
                        value.DeviceGridOffset,
                        scale.Value);
                    using var canvas = ImmediateCanvas.CreateExecutorManaged(
                        value.Target,
                        scale.Value,
                        _options.MaxWorkingScale,
                        value.RasterBounds.Size,
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

        private IReadOnlyList<CompatibilityRenderValue> MaterializeOpacityMask(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
        {
            if (fragment.Inputs.Length == 0)
                throw new InvalidOperationException("An opacity mask requires a primary input.");
            if (fragment.Bounds.Width == 0 || fragment.Bounds.Height == 0)
            {
                foreach (RenderFragmentReference input in fragment.Inputs)
                    CompleteFragmentUse(input);
                MarkExecutionSkipped(fragment);
                return [];
            }

            EffectiveScale scale = ClampToActiveDeviceGrid(
                fragment.Bounds,
                requestedScale ?? ResolveConcreteScale(fragment));
            var maskValues = new List<CompatibilityRenderValue>();
            int materializedDependencyCount = 0;
            bool primaryMaterialized = false;
            try
            {
                for (int index = 1; index < fragment.Inputs.Length; index++)
                {
                    RenderFragmentReference dependency = fragment.Inputs[index];
                    maskValues.AddRange(Materialize(
                        dependency,
                        currentTarget,
                        dependency.EffectiveScale.IsUnbounded ? scale : null));
                    materializedDependencyCount++;
                }

                RenderFragmentReference primary = fragment.Inputs[0];
                IReadOnlyList<CompatibilityRenderValue> primaryValues = Materialize(
                    primary,
                    currentTarget,
                    primary.EffectiveScale.IsUnbounded ? scale : null);
                primaryMaterialized = true;
                CompatibilityRenderValue value = CreateOwnedValue(
                    fragment.Bounds,
                    scale,
                    allowPreviewDrop: _previewDropEligibleMaterializations.Contains(fragment));
                _diagnostics?.RecordGpuPassExecuted(fragment.Id?.Value ?? 0);
                bool succeeded = false;
                try
                {
                    Vector rasterTranslation = DeviceGridAlignment.ResolveRasterTranslation(
                        value.DeviceBounds,
                        value.DeviceGridOffset,
                        scale.Value);
                    using var canvas = ImmediateCanvas.CreateExecutorManaged(
                        value.Target,
                        scale.Value,
                        _options.MaxWorkingScale,
                        value.RasterBounds.Size,
                        value.DeviceBounds.Position);
                    using (canvas.PushTransform(Matrix.CreateTranslation(
                               rasterTranslation.X,
                               rasterTranslation.Y)))
                    {
                        var payload = (OpacityMaskRenderFragmentPayload)fragment.Payload!;
                        var images = new List<SKImage>();
                        var token = new RenderExecutionSessionToken();
                        try
                        {
                            token.RunAndComplete(
                                () =>
                                {
                                    IReadOnlyList<RenderExecutionInput> inputs = CreateExecutionInputs(
                                        token,
                                        maskValues,
                                        requiresReadback: false,
                                        readbackOwner: null,
                                        images);
                                    BrushExecutionResolver.UseBrush(
                                        token,
                                        payload.Resources,
                                        inputs,
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
                                        });
                                });
                        }
                        finally
                        {
                            foreach (SKImage image in images)
                                image.Dispose();
                        }
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
                if (primaryMaterialized)
                    CompleteFragmentUse(fragment.Inputs[0]);
                for (int index = 1; index <= materializedDependencyCount; index++)
                    CompleteFragmentUse(fragment.Inputs[index]);
            }
        }

        private IReadOnlyList<CompatibilityRenderValue> ExecuteOpaque(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
            => ExecuteOnDeviceGrid(
                currentTarget,
                () => ExecuteOpaqueCore(fragment, currentTarget, requestedScale));

        private IReadOnlyList<CompatibilityRenderValue> ExecuteOpaqueCore(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
        {
            var payload = (OpaqueRenderFragmentPayload)fragment.Payload!;
            OpaqueRenderDescription description = payload.Description;
            var flattened = new List<CompatibilityRenderValue>();
            var inputReadbacks = new List<bool>();
            var inputRanges = new List<RenderExecutionInputRange>(fragment.Inputs.Length);
            EffectiveScale outputSupply = requestedScale
                ?? (!fragment.EffectiveScale.IsUnbounded
                    ? fragment.EffectiveScale
                    : EffectiveScale.At(currentTarget.Density));
            for (int inputIndex = 0; inputIndex < fragment.Inputs.Length; inputIndex++)
            {
                RenderFragmentReference input = fragment.Inputs[inputIndex];
                IReadOnlyList<CompatibilityRenderValue> inputValues = Materialize(
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
                    var mapped = new List<CompatibilityRenderValue>();
                    bool mapCallbackInvoked = false;
                    for (int inputIndex = 0; inputIndex < flattened.Count; inputIndex++)
                    {
                        CompatibilityRenderValue input = flattened[inputIndex];
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
                    flattened.Select(static value => value.CompleteBounds).ToArray());
                EffectiveScale declaredScale = requestedScale
                    ?? description.Scale.Resolve(
                        flattened.Select(static value => value.EffectiveScale).ToArray(),
                        declaredBounds,
                        _options.OutputScale,
                        _options.MaxWorkingScale);
                IReadOnlyList<CompatibilityRenderValue> result = InvokeOpaque(
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

        private IReadOnlyList<CompatibilityRenderValue> ExecuteLegacyFilter(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget)
            => ExecuteOnDeviceGrid(
                currentTarget,
                () => ExecuteLegacyFilterCore(fragment, currentTarget));

        private IReadOnlyList<CompatibilityRenderValue> ExecuteLegacyFilterCore(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget)
        {
            Rect requiredRegion = ResolveFragmentRequirement(fragment, fragment.Bounds);
            var payload = (LegacyFilterEffectRenderFragmentPayload)fragment.Payload!;
            var inputs = new List<CompatibilityRenderValue>();
            var brushValues = new List<CompatibilityRenderValue>();
            EffectiveScale inputRequestScale = fragment.EffectiveScale.IsUnbounded
                ? EffectiveScale.At(currentTarget.Density)
                : fragment.EffectiveScale;
            for (int index = 0; index < fragment.Inputs.Length; index++)
            {
                RenderFragmentReference input = fragment.Inputs[index];
                List<CompatibilityRenderValue> destination =
                    index < payload.StreamInputCount ? inputs : brushValues;
                destination.AddRange(Materialize(
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
                        _diagnostics?.RecordOpaqueExecution(fragment.Id?.Value ?? 0);
                        using var targets = new EffectTargets();
                        foreach (CompatibilityRenderValue input in inputs)
                        {
                            targets.Add(new EffectTarget(
                                input.Target,
                                input.Bounds,
                                input.EffectiveScale,
                                input.DeviceBounds,
                                input.DeviceGridOffset)
                            {
                                OriginalBounds = new Rect(default, input.Bounds.Size),
                                Bounds = input.Bounds,
                            });
                        }

                        using var builder = new SKImageFilterBuilder();
                        return RunWithResolvedBrushes(
                            payload,
                            brushValues,
                            brushes =>
                            {
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
                                        fragment.Id?.Value ?? 0,
                                        target,
                                        source),
                                    brushes);
                                activator.Apply(effectContext);
                                activator.CompletePolicyBoundary(
                                    payload.WorkingScalePolicy.HasValue);

                                var result = new List<CompatibilityRenderValue>(activator.CurrentTargets.Count);
                                foreach (EffectTarget target in activator.CurrentTargets)
                                {
                                    if (target.RenderTarget is not { } renderTarget)
                                        continue;

                                    CompatibilityRenderValue value = MaterializeLegacyTarget(
                                        target,
                                        renderTarget,
                                        fragment.Bounds);
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
                                        if (value.PreserveLegacyRasterPlacement)
                                        {
                                            // A legacy raster-placement value draws from its allocation
                                            // footprint rather than from Bounds, so narrowing it is a
                                            // relabel that must not re-allocate or move the placement.
                                            value.Bounds = selectedBounds;
                                        }
                                        else
                                        {
                                            CompatibilityRenderValue cropped = CropValue(
                                                fragment,
                                                value,
                                                selectedBounds);
                                            ReleaseUnpublished(value);
                                            value = cropped;
                                        }
                                    }

                                    result.Add(value);
                                }

                                return (IReadOnlyList<CompatibilityRenderValue>)result;
                            });
                    });
            }
            finally
            {
                foreach (RenderFragmentReference input in fragment.Inputs)
                    CompleteFragmentUse(input);
            }
        }

        private T RunWithResolvedBrushes<T>(
            LegacyFilterEffectRenderFragmentPayload payload,
            IReadOnlyList<CompatibilityRenderValue> brushValues,
            Func<IReadOnlyDictionary<FilterEffectBrush, ResolvedBrush>?, T> use)
        {
            if (payload.Brushes.IsDefaultOrEmpty)
                return use(null);

            var images = new List<SKImage>();
            var token = new RenderExecutionSessionToken();
            T result = default!;
            bool used = false;
            try
            {
                token.RunAndComplete(
                    () =>
                    {
                        IReadOnlyList<RenderExecutionInput> inputs = CreateExecutionInputs(
                            token,
                            brushValues,
                            requiresReadback: false,
                            readbackOwner: null,
                            images);
                        var resolved = new Dictionary<FilterEffectBrush, ResolvedBrush>();
                        ResolveBrush(
                            token,
                            payload,
                            inputs,
                            resolved,
                            0,
                            brushes =>
                            {
                                result = use(brushes);
                                used = true;
                            });
                    });
            }
            finally
            {
                foreach (SKImage image in images)
                    image.Dispose();
            }

            if (!used)
            {
                throw new InvalidOperationException(
                    "A legacy filter-effect brush binding never produced a resolved brush, so the effect could "
                    + "not execute.");
            }

            return result;
        }

        private static void ResolveBrush(
            RenderExecutionSessionToken token,
            LegacyFilterEffectRenderFragmentPayload payload,
            IReadOnlyList<RenderExecutionInput> inputs,
            Dictionary<FilterEffectBrush, ResolvedBrush> resolved,
            int index,
            Action<IReadOnlyDictionary<FilterEffectBrush, ResolvedBrush>?> use)
        {
            if (index >= payload.Brushes.Length)
            {
                use(resolved);
                return;
            }

            LegacyFilterEffectBrushBinding binding = payload.Brushes[index];
            BrushExecutionResolver.UseBrush(
                token,
                payload.BrushResources,
                inputs,
                binding.Brush,
                brush =>
                {
                    resolved[binding.Handle] = brush;
                    ResolveBrush(token, payload, inputs, resolved, index + 1, use);
                });
        }

        private CompatibilityRenderValue MaterializeLegacyTarget(
            EffectTarget target,
            RenderTarget renderTarget,
            Rect completeBounds)
        {
            if (target.PreserveLegacyRasterPlacement)
            {
                Vector deviceGridOffset = target.DeviceBounds
                    .ToRect(target.Scale.Value)
                    .Position - target.RasterBounds.Position;
                return CreateOwnedShallowCopy(
                    renderTarget,
                    target.Bounds,
                    target.Scale,
                    target.DeviceBounds,
                    deviceGridOffset,
                    completeBounds,
                    preserveLegacyRasterPlacement: true);
            }

            Rect canonicalRasterBounds = target.DeviceBounds
                .ToRect(target.Scale.Value)
                .Translate(-target.DeviceGridOffset);
            PixelRect semanticDeviceBounds = PixelRect.FromRect(
                target.Bounds.Translate(target.DeviceGridOffset),
                target.Scale.Value);
            if (target.RasterBounds == canonicalRasterBounds
                && Contains(target.DeviceBounds, semanticDeviceBounds))
            {
                return CreateOwnedShallowCopy(
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
            CompatibilityRenderValue normalized = CreateOwnedValue(
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
                using var canvas = ImmediateCanvas.CreateExecutorManaged(
                    normalized.Target,
                    normalized.EffectiveScale.Value,
                    _options.MaxWorkingScale,
                    normalized.RasterBounds.Size,
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

        private static bool Contains(PixelRect outer, PixelRect inner)
            => outer.X <= inner.X
               && outer.Y <= inner.Y
               && outer.Right >= inner.Right
               && outer.Bottom >= inner.Bottom;

    }
}
