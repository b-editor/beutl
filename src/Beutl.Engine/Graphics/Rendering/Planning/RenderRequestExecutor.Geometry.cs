using System.Collections.Immutable;
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
        private IReadOnlyList<MaterializedRenderValue> ExecuteGeometry(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget)
            => ExecuteOnDeviceGrid(
                currentTarget,
                () => ExecuteGeometryCore(fragment, currentTarget));

        private IReadOnlyList<MaterializedRenderValue> ExecuteGeometryCore(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget)
        {
            if (fragment.Inputs.Length != 1)
                throw new InvalidOperationException("A Geometry fragment requires exactly one input stream.");

            GeometryDescription description = ((GeometryRenderFragmentPayload)fragment.Payload!).Description;
            EffectiveScale requestScale = fragment.EffectiveScale.IsUnbounded
                ? EffectiveScale.At(currentTarget.Density)
                : fragment.EffectiveScale;
            IReadOnlyList<MaterializedRenderValue> inputs = Materialize(
                fragment.Inputs[0],
                currentTarget,
                fragment.Inputs[0].EffectiveScale.IsUnbounded ? requestScale : null);
            var results = new List<MaterializedRenderValue>(inputs.Count);
            bool executed = false;
            try
            {
                foreach (MaterializedRenderValue input in inputs)
                {
                    Rect outputBounds = description.Bounds.TransformBounds(input.CompleteBounds);
                    if (outputBounds.Width == 0 || outputBounds.Height == 0)
                        continue;

                    Rect requiredRegion = ResolveFragmentRequirement(fragment, outputBounds);
                    if (requiredRegion.Width == 0 || requiredRegion.Height == 0)
                        continue;

                    float density = requestScale.Value;
                    density = RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(
                        outputBounds.Translate(_activeDeviceGridOffset),
                        density);
                    EffectiveScale outputScale = EffectiveScale.At(density);
                    MaterializedRenderValue output = CreateOwnedValue(
                        requiredRegion,
                        outputScale,
                        outputBounds,
                        allowPreviewDrop: true);
                    bool keepOutput = false;
                    try
                    {
                        Rect? finalBounds = ExecuteGeometryElement(
                            fragment,
                            description,
                            input,
                            output,
                            outputBounds,
                            requiredRegion);
                        executed = true;
                        if (finalBounds is not { Width: > 0, Height: > 0 } selectedBounds)
                            continue;

                        if (selectedBounds != requiredRegion)
                        {
                            MaterializedRenderValue cropped = CropValue(
                                output,
                                selectedBounds,
                                allowPreviewDrop: true);
                            ReleaseUnpublished(output);
                            output = cropped;
                        }

                        results.Add(output);
                        keepOutput = true;
                    }
                    finally
                    {
                        if (!keepOutput)
                            ReleaseUnpublished(output);
                    }
                }

                if (!executed)
                    MarkExecutionSkipped(fragment);
                return results;
            }
            catch
            {
                foreach (MaterializedRenderValue value in results)
                    ReleaseUnpublished(value);
                throw;
            }
            finally
            {
                CompleteFragmentUse(fragment.Inputs[0]);
            }
        }

        private Rect? ExecuteGeometryElement(
            RenderFragmentReference fragment,
            GeometryDescription description,
            MaterializedRenderValue input,
            MaterializedRenderValue output,
            Rect outputBounds,
            Rect requiredRegion)
        {
            using SKImage inputImage = input.Target.Value.Snapshot();
            RenderExecutionSessionToken token = CreateExecutionSessionToken();
            return token.RunAndComplete<Rect?>(
                () =>
                {
                    Func<Bitmap>? createSnapshot = description.RequiresReadback
                        ? () => SnapshotInputForReadback(input)
                        : null;
                    var executionInput = new RenderExecutionInput(
                        token,
                        input.Bounds,
                        input.EffectiveScale,
                        input.DeviceBounds,
                        input.RasterBounds,
                        inputImage,
                        createSnapshot,
                        description.RequiresReadback);
                    var callbackCanvas = new RenderCallbackCanvas(
                        token,
                        output.EffectiveScale.Value,
                        requiredRegion,
                        output.DeviceBounds,
                        () => CreateExecutorCanvas(
                            output.Target,
                            output.EffectiveScale.Value,
                            _options.MaxWorkingScale,
                            output.RasterBounds.Size,
                            _options.Intent,
                            output.DeviceBounds.Position),
                        CallbackCanvasCapability.Draw,
                        rasterBounds: output.RasterBounds);
                    var session = new GeometrySession(
                        token,
                        executionInput,
                        outputBounds,
                        requiredRegion,
                        output.DeviceBounds,
                        _options.OutputScale,
                        output.EffectiveScale.Value,
                        _options.MaxWorkingScale,
                        _options.Intent,
                        _options.Purpose,
                        callbackCanvas,
                        description.Resources);
                    description.Render(session);
                    if (session.IsOutputDiscarded)
                        return null;

                    return session.OutputBounds.Intersect(requiredRegion);
                });
        }

        private MaterializedRenderValue CropValue(
            RenderFragmentReference fragment,
            MaterializedRenderValue source,
            Rect selectedBounds)
            => CropValue(
                source,
                selectedBounds,
                _previewDropEligibleMaterializations.Contains(fragment));

        private MaterializedRenderValue CropValue(
            MaterializedRenderValue source,
            Rect selectedBounds,
            bool allowPreviewDrop)
        {
            MaterializedRenderValue cropped = CreateOwnedValue(
                selectedBounds,
                source.EffectiveScale,
                source.CompleteBounds,
                deviceGridOffset: source.DeviceGridOffset,
                allowPreviewDrop: allowPreviewDrop);
            bool succeeded = false;
            try
            {
                Vector rasterTranslation = DeviceGridAlignment.ResolveRasterTranslation(
                    cropped.DeviceBounds,
                    cropped.DeviceGridOffset,
                    cropped.EffectiveScale.Value);
                using var canvas = CreateExecutorCanvas(
                    cropped.Target,
                    cropped.EffectiveScale.Value,
                    _options.MaxWorkingScale,
                    cropped.RasterBounds.Size,
                    _options.Intent,
                    cropped.DeviceBounds.Position);
                using (canvas.PushTransform(Matrix.CreateTranslation(
                           rasterTranslation.X,
                           rasterTranslation.Y)))
                {
                    canvas.ClipRect(selectedBounds);
                    canvas.DrawRenderTargetScaledWithoutFlush(source.Target, source.RasterBounds);
                }
                succeeded = true;
                return cropped;
            }
            finally
            {
                if (!succeeded)
                    ReleaseUnpublished(cropped);
            }
        }

        private IReadOnlyList<MaterializedRenderValue> InvokeOpaque(
            RenderFragmentReference fragment,
            OpaqueRenderDescription description,
            IReadOnlyList<MaterializedRenderValue> inputs,
            IReadOnlyList<bool> inputReadbacks,
            IReadOnlyList<RenderExecutionInputRange> inputRanges,
            Rect outputBounds,
            EffectiveScale declaredScale,
            RenderValueCardinality cardinality,
            out bool callbackInvoked)
        {
            callbackInvoked = false;
            Rect requiredRegion = ResolveFragmentRequirement(fragment, outputBounds);
            if (requiredRegion.Width == 0 || requiredRegion.Height == 0)
                return [];

            var inputImages = new List<SKImage>();
            var executionInputs = new List<RenderExecutionInput>(inputs.Count);
            var outputLeases = new Dictionary<OpaqueRenderOutput, MaterializedRenderValue>(
                ReferenceEqualityComparer.Instance);
            var published = new List<MaterializedRenderValue>();
            bool succeeded = false;
            bool callbackWasInvoked = false;
            RenderExecutionSessionToken token = CreateExecutionSessionToken();
            try
            {
                IReadOnlyList<MaterializedRenderValue> result = token.RunAndComplete(
                    () =>
                    {
                        for (int inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
                        {
                            MaterializedRenderValue input = inputs[inputIndex];
                            bool requiresReadback = inputReadbacks[inputIndex];
                            SKImage image = input.Target.Value.Snapshot();
                            inputImages.Add(image);
                            Func<Bitmap>? createSnapshot = requiresReadback
                                ? () => SnapshotInputForReadback(input)
                                : null;
                            executionInputs.Add(new RenderExecutionInput(
                                token,
                                input.Bounds,
                                input.EffectiveScale,
                                input.DeviceBounds,
                                input.RasterBounds,
                                image,
                                createSnapshot,
                                requiresReadback));
                        }

                        float density = declaredScale.IsUnbounded
                            ? RenderScaleUtilities.ResolveWorkingScale(
                                inputs.SelectToArray(static value => value.EffectiveScale),
                                _options.OutputScale,
                                _options.MaxWorkingScale)
                            : declaredScale.Value;
                        // A source whose rasterization reaches outside the bounds it publishes declares the
                        // extra room here rather than publishing the wider rectangle, which would move it.
                        Thickness rasterOutset = fragment.Kind == RenderFragmentKind.OpaqueSource
                            ? description.Bounds.RasterOutset
                            : default;
                        density = RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(
                            outputBounds.Inflate(rasterOutset).Translate(_activeDeviceGridOffset),
                            density);
                        bool preserveRasterApron = description.HasDirectReplayMaterializationContract
                                                   && fragment.Kind == RenderFragmentKind.OpaqueSource;
                        density = RenderMaterializationDensityPolicy.Clamp(
                            fragment,
                            density);
                        if (preserveRasterApron)
                        {
                            density = RenderScaleUtilities.ClampWorkingScaleToRasterApronBudget(
                                outputBounds.Inflate(rasterOutset).Translate(_activeDeviceGridOffset),
                                density);
                        }

                        OpaqueRenderSession? session = null;
                        session = new OpaqueRenderSession(
                            token,
                            executionInputs,
                            inputRanges,
                            outputBounds,
                            requiredRegion,
                            PixelRect.FromRect(
                                requiredRegion.Translate(_activeDeviceGridOffset),
                                density),
                            _options.OutputScale,
                            density,
                            _options.MaxWorkingScale,
                            _options.Intent,
                            _options.Purpose,
                            description.Resources,
                            (_, logicalBounds, requestedOutputDensity) =>
                            {
                                float outputDensity = requestedOutputDensity is { } requested
                                    ? Math.Min(requested, _options.MaxWorkingScale)
                                    : density;
                                if (requestedOutputDensity.HasValue)
                                {
                                    Rect densityBounds = logicalBounds
                                        .Inflate(rasterOutset)
                                        .Translate(_activeDeviceGridOffset);
                                    outputDensity = preserveRasterApron
                                        ? RenderScaleUtilities.ClampWorkingScaleToRasterApronBudget(
                                            densityBounds,
                                            outputDensity)
                                        : RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(
                                            densityBounds,
                                            outputDensity);
                                }

                                EffectiveScale outputScale = EffectiveScale.At(outputDensity);
                                Rect rasterFootprint = logicalBounds.Inflate(rasterOutset);
                                PixelRect? physicalDeviceBounds = preserveRasterApron
                                    ? RenderScaleUtilities.AddRasterApron(
                                        PixelRect.FromRect(rasterFootprint, outputDensity))
                                    : rasterOutset == default
                                        ? null
                                        : PixelRect.FromRect(rasterFootprint, outputDensity);
                                MaterializedRenderValue value = CreateOwnedValue(
                                    logicalBounds,
                                    outputScale,
                                    outputBounds,
                                    physicalDeviceBounds,
                                    allowPreviewDrop: _previewDropEligibleMaterializations.Contains(fragment));
                                var canvas = new RenderCallbackCanvas(
                                    token,
                                    outputDensity,
                                    logicalBounds,
                                    value.DeviceBounds,
                                    () => CreateExecutorCanvas(
                                        value.Target,
                                        outputDensity,
                                        _options.MaxWorkingScale,
                                        value.RasterBounds.Size,
                                        _options.Intent,
                                        value.DeviceBounds.Position),
                                    CallbackCanvasCapability.Draw,
                                    rasterBounds: value.RasterBounds);
                                var output = new OpaqueRenderOutput(
                                    token,
                                    session!,
                                    logicalBounds,
                                    outputScale,
                                    canvas,
                                    _ => ReleaseUnpublished(value));
                                outputLeases.Add(output, value);
                                return output;
                            },
                            output =>
                            {
                                MaterializedRenderValue value = outputLeases[output];

                                // Empty bounds mean no output; publishing would request an invalid zero-extent crop.
                                if (output.Bounds.Width <= 0 || output.Bounds.Height <= 0)
                                    return;

                                if (value.Bounds != output.Bounds)
                                {
                                    MaterializedRenderValue cropped = CropValue(
                                        value,
                                        output.Bounds,
                                        allowPreviewDrop: _previewDropEligibleMaterializations.Contains(fragment));
                                    ReleaseUnpublished(value);
                                    outputLeases[output] = cropped;
                                    value = cropped;
                                }
                                published.Add(value);
                            });

                        callbackWasInvoked = true;
                        description.Execute(session);
                        ValidateOutputCount(cardinality, published.Count);
                        if (description.BackendBoundary != RenderBackendBoundary.None && published.Count != 0)
                        {
                            RecordSynchronization();
                        }
                        return published.ToArray();
                    });
                succeeded = true;
                return result;
            }
            finally
            {
                callbackInvoked = callbackWasInvoked;
                foreach (SKImage image in inputImages)
                    image.Dispose();

                foreach (MaterializedRenderValue value in outputLeases.Values)
                {
                    // MaterializedRenderValue does not override Equals, so the list's own Contains already
                    // compares by reference - the LINQ form only adds a boxed enumerator.
                    if (!succeeded || !published.Contains(value))
                        ReleaseUnpublished(value);
                }
            }
        }

        private IReadOnlyList<MaterializedRenderValue> MaterializeExternal(
            RenderFragmentReference fragment)
        {
            var payload = (MaterializedInputRenderFragmentPayload)fragment.Payload!;
            MaterializedInputDescription description = payload.Description;
            MaterializedRenderValue value = description.Target.Registry.Use(
                description.Target,
                target =>
                {
                    description.ValidateTargetDeviceSize(target);
                    return CreateOwnedShallowCopy(
                        target,
                        description.Bounds,
                        description.EffectiveScale,
                        description.DeviceBounds,
                        description.DeviceGridOffset);
                });
            _ownedValues.Add(value);
            return [value];
        }

    }
}
