using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RenderRequestExecutor
{
    private sealed partial class RenderRequestExecutionState
    {
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
            RenderValueCardinality cardinality)
        {
            Rect requiredRegion = ResolveFragmentRequirement(fragment, outputBounds);
            if (requiredRegion.Width == 0 || requiredRegion.Height == 0)
                return [];

            var inputImages = new List<SKImage>();
            var executionInputs = new List<RenderExecutionInput>(inputs.Count);
            var outputLeases = new Dictionary<OpaqueRenderOutput, MaterializedRenderValue>(
                ReferenceEqualityComparer.Instance);
            var published = new List<MaterializedRenderValue>();
            bool succeeded = false;
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
                            executionInputs.Add(CreateExecutionInput(token, input, image, requiresReadback));
                        }

                        // A source whose rasterization reaches outside the bounds it publishes declares the
                        // extra room here rather than publishing the wider rectangle, which would move it.
                        Thickness rasterOutset = fragment.Kind == RenderFragmentKind.OpaqueSource
                            ? description.Bounds.RasterOutset
                            : default;
                        bool preserveRasterApron = description.HasDirectReplayMaterializationContract
                                                   && fragment.Kind == RenderFragmentKind.OpaqueSource;
                        float density = ResolveOpaqueWorkingDensity(
                            fragment,
                            inputs,
                            declaredScale,
                            outputBounds,
                            rasterOutset,
                            preserveRasterApron);

                        var session = new OpaqueRenderSession(
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
                            (owner, logicalBounds, requestedOutputDensity) =>
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
                                        ? BufferDimensionBudget.EngineCeiling.ClampWorkingScaleToRasterApron(
                                            densityBounds,
                                            outputDensity)
                                        : BufferDimensionBudget.EngineCeiling.ClampWorkingScaleToExactFootprint(
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
                                    owner,
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
                foreach (SKImage image in inputImages)
                    image.Dispose();

                foreach (MaterializedRenderValue value in outputLeases.Values)
                {
                    if (!succeeded || !published.Contains(value))
                        ReleaseUnpublished(value);
                }
            }
        }

        private float ResolveOpaqueWorkingDensity(
            RenderFragmentReference fragment,
            IReadOnlyList<MaterializedRenderValue> inputs,
            EffectiveScale declaredScale,
            Rect outputBounds,
            Thickness rasterOutset,
            bool preserveRasterApron)
        {
            float density = declaredScale.IsUnbounded
                ? RenderScaleUtilities.ResolveWorkingScale(
                    inputs.SelectToArray(static value => value.EffectiveScale),
                    _options.OutputScale,
                    _options.MaxWorkingScale)
                : declaredScale.Value;
            density = BufferDimensionBudget.EngineCeiling.ClampWorkingScaleToExactFootprint(
                outputBounds.Inflate(rasterOutset).Translate(_activeDeviceGridOffset),
                density);
            density = RenderMaterializationDensityPolicy.Clamp(
                fragment,
                density);
            if (preserveRasterApron)
            {
                density = BufferDimensionBudget.EngineCeiling.ClampWorkingScaleToRasterApron(
                    outputBounds.Inflate(rasterOutset).Translate(_activeDeviceGridOffset),
                    density);
            }

            return density;
        }
    }
}
