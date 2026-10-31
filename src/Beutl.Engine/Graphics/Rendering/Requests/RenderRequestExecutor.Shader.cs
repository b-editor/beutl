using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RenderRequestExecutor
{
    private sealed partial class RenderRequestExecutionState
    {
        private IReadOnlyList<MaterializedRenderValue> ExecuteShader(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
            => ExecuteOnDeviceGrid(
                currentTarget,
                () => ExecuteShaderCore(fragment, currentTarget, requestedScale));

        private IReadOnlyList<MaterializedRenderValue> ExecuteShaderCore(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
        {
            if (fragment.Inputs.Length != 1)
                throw new InvalidOperationException("A Shader fragment requires exactly one input stream.");

            var payload = (ShaderRenderFragmentPayload)fragment.Payload!;
            ShaderDescription description = payload.Description;
            EffectiveScale inputRequestScale = requestedScale
                ?? (!fragment.EffectiveScale.IsUnbounded
                    ? fragment.EffectiveScale
                    : EffectiveScale.At(currentTarget.Density));
            IReadOnlyList<MaterializedRenderValue> inputs = Materialize(
                fragment.Inputs[0],
                currentTarget,
                fragment.Inputs[0].EffectiveScale.IsUnbounded
                    ? ResolveInputCallerScale(description.InputDemand, 0, inputRequestScale)
                    : null);
            var results = new List<MaterializedRenderValue>(inputs.Count);
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

                    float density = !fragment.EffectiveScale.IsUnbounded
                        ? fragment.EffectiveScale.Value
                        : inputRequestScale.Value;
                    density = BufferDimensionBudget.EngineCeiling.ClampWorkingScaleToExactFootprint(
                        outputBounds.Translate(_activeDeviceGridOffset),
                        density);
                    EffectiveScale outputScale = EffectiveScale.At(density);
                    MaterializedRenderValue output = CreateOwnedValue(
                        requiredRegion,
                        outputScale,
                        outputBounds,
                        allowPreviewDrop: true);
                    bool succeeded = false;
                    try
                    {
                        MaterializedRenderValue shaderInput = NormalizeSemanticShaderInput(input);
                        try
                        {
                            ExecuteShaderElement(
                                description,
                                shaderInput,
                                output,
                                outputBounds,
                                requiredRegion);
                        }
                        finally
                        {
                            if (!ReferenceEquals(shaderInput, input))
                                ReleaseUnpublished(shaderInput);
                        }

                        results.Add(output);
                        succeeded = true;
                    }
                    finally
                    {
                        if (!succeeded)
                            ReleaseUnpublished(output);
                    }
                }

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

        private MaterializedRenderValue NormalizeSemanticShaderInput(MaterializedRenderValue input)
        {
            if (!input.PreserveImperativeRasterPlacement || CanUseAsSemanticShaderInput(input))
                return input;

            MaterializedRenderValue normalized = CreateNormalizedValue(
                input.Target,
                input.RasterBounds,
                input.Bounds,
                input.EffectiveScale,
                input.DeviceGridOffset,
                input.CompleteBounds,
                addRasterApron: false);
            if (CanUseAsSemanticShaderInput(normalized))
                return normalized;

            // Global device-grid alignment can move a locally exact edge by a sub-pixel epsilon.
            // Keep the effect-item exact placement whenever it is sufficient, and add an apron only for
            // the residual case where raster-local pixel rounding still falls outside the image.
            ReleaseUnpublished(normalized);
            return CreateNormalizedValue(
                input.Target,
                input.RasterBounds,
                input.Bounds,
                input.EffectiveScale,
                input.DeviceGridOffset,
                input.CompleteBounds,
                addRasterApron: true);
        }

        // Redraws a source onto a canonical device grid at a density that fits its physical footprint.
        private MaterializedRenderValue CreateNormalizedValue(
            RenderTarget source,
            Rect rasterBounds,
            Rect bounds,
            EffectiveScale scale,
            Vector deviceGridOffset,
            Rect completeBounds,
            bool addRasterApron)
        {
            Rect physicalBounds = rasterBounds.Union(bounds);
            Rect alignedPhysicalBounds = physicalBounds.Translate(deviceGridOffset);
            float density = addRasterApron
                ? BufferDimensionBudget.EngineCeiling.ClampWorkingScaleToRasterApron(
                    alignedPhysicalBounds,
                    scale.Value)
                : BufferDimensionBudget.EngineCeiling.ClampWorkingScaleToExactFootprint(
                    alignedPhysicalBounds,
                    scale.Value);
            EffectiveScale normalizedScale = EffectiveScale.At(density);
            PixelRect normalizedDeviceBounds = PixelRect.FromRect(physicalBounds, density);
            if (addRasterApron)
                normalizedDeviceBounds = RenderScaleUtilities.AddRasterApron(normalizedDeviceBounds);
            MaterializedRenderValue normalized = CreateOwnedValue(
                bounds,
                normalizedScale,
                completeBounds,
                physicalDeviceBounds: normalizedDeviceBounds,
                deviceGridOffset: deviceGridOffset,
                allowPreviewDrop: true);
            bool succeeded = false;
            try
            {
                using var canvas = CreateValueCanvas(normalized);
                using (canvas.PushTransform(normalized.RasterAlignmentTransform))
                {
                    canvas.DrawRenderTargetScaledWithoutFlush(source, rasterBounds);
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

        private static bool CanUseAsSemanticShaderInput(MaterializedRenderValue input)
        {
            // Mirror RasterShaderMapping's semantic subset calculation. Continuous Rect
            // containment is insufficient because PixelRect.FromRect rounds both edges outward.
            Rect sourceRasterBounds = input.RasterBounds;
            float sourceScale = input.EffectiveScale.Value;
            Rect canonicalRasterBounds = input.DeviceBounds.ToRect(sourceScale);
            PixelRect semanticDeviceBounds;
            if (sourceRasterBounds == canonicalRasterBounds)
            {
                semanticDeviceBounds = PixelRect.FromRect(input.Bounds, sourceScale);
            }
            else
            {
                Vector deviceGridOffset = canonicalRasterBounds.Position - sourceRasterBounds.Position;
                semanticDeviceBounds = PixelRect.FromRect(
                    input.Bounds.Translate(deviceGridOffset),
                    sourceScale);
            }

            var semanticSubset = new PixelRect(
                semanticDeviceBounds.X - input.DeviceBounds.X,
                semanticDeviceBounds.Y - input.DeviceBounds.Y,
                semanticDeviceBounds.Width,
                semanticDeviceBounds.Height);
            var imageBounds = new PixelRect(input.DeviceBounds.Size);
            return imageBounds.Contains(semanticSubset);
        }

        private ProgramCacheLease<CachedSkRuntimeEffect> AcquireStandaloneProgram(
            EffectTarget target,
            string source)
        {
            ArgumentNullException.ThrowIfNull(target);
            RenderTarget renderTarget = target.RenderTarget
                ?? throw new InvalidOperationException(
                    "An effect-item shader program requires a materialized execution destination.");
            return AcquireStandaloneProgram(renderTarget, source);
        }

        private ProgramCacheLease<CachedSkRuntimeEffect> AcquireStandaloneProgram(
            RenderTarget target,
            string source)
        {
            ProgramCacheLease<CachedSkRuntimeEffect> lease =
                SkRuntimeEffectProgramCache.AcquireForDestination(
                    _programCaches.Sksl,
                    target,
                    source);
            if (lease.IsCacheHit)
                _programCacheHits++;
            return lease;
        }

        /// <summary>Repaints one materialized value's whole buffer with <paramref name="shader"/>.</summary>
        private void PaintOverValue(MaterializedRenderValue output, SKShader shader)
        {
            using var paint = new SKPaint { Shader = shader };
            using var canvas = CreateValueCanvas(output);
            canvas.Clear();
            using (canvas.PushDeviceSpace())
            {
                canvas.Canvas.DrawRect(
                    SKRect.Create(output.Target.Width, output.Target.Height),
                    paint);
            }
        }

        private void ExecuteShaderElement(
            ShaderDescription description,
            MaterializedRenderValue input,
            MaterializedRenderValue output,
            Rect outputBounds,
            Rect requiredRegion)
        {
            using SurfaceSnapshot.Lease inputSnapshot = SurfaceSnapshot.Take(input.Target.Value);
            SKImage inputImage = inputSnapshot.Image;
            ShaderEvaluationFrame frame = description.Kind == ShaderDescriptionKind.WholeSource
                ? RasterShaderMapping.CreateWholeSourceFrame(
                    outputBounds,
                    output.DeviceBounds,
                    output.RasterBounds,
                    output.EffectiveScale.Value)
                : ShaderEvaluationFrame.Destination(output.DeviceBounds, output.RasterBounds);
            StandaloneShaderProgram program = StandaloneShaderProgram.For(description);
            using ProgramCacheLease<CachedSkRuntimeEffect> lease =
                AcquireStandaloneProgram(output.Target, program.Source);
            using var uniforms = new SKRuntimeEffectUniforms(lease.Program.Effect);
            using var runtimeChildren = new SKRuntimeEffectChildren(lease.Program.Effect);
            var children = new List<SKShader>();
            try
            {
                if (description.HasExecutionContextBinder)
                {
                    BindStandaloneShaderWithCallbacks(
                        description,
                        input,
                        output,
                        outputBounds,
                        requiredRegion,
                        inputImage,
                        frame,
                        program,
                        uniforms,
                        runtimeChildren,
                        children);
                }
                else
                {
                    BindStandaloneShaderCore(
                        description,
                        context: null,
                        input,
                        output,
                        inputImage,
                        frame,
                        program,
                        uniforms,
                        runtimeChildren,
                        children);
                }

                using SKShader shader = lease.Program.Effect.ToShader(uniforms, runtimeChildren);
                PaintInEvaluationFrame(output, shader, frame);
            }
            finally
            {
                for (int index = children.Count - 1; index >= 0; index--)
                    children[index].Dispose();
            }
        }

        private void BindStandaloneShaderWithCallbacks(
            ShaderDescription description,
            MaterializedRenderValue input,
            MaterializedRenderValue output,
            Rect outputBounds,
            Rect requiredRegion,
            SKImage inputImage,
            ShaderEvaluationFrame frame,
            StandaloneShaderProgram program,
            SKRuntimeEffectUniforms uniforms,
            SKRuntimeEffectChildren runtimeChildren,
            List<SKShader> children)
        {
            RenderExecutionSessionToken bindingToken = CreateExecutionSessionToken();
            bindingToken.RunAndComplete(
                () =>
                {
                    var context = new ShaderExecutionContext(
                        bindingToken,
                        input.Bounds,
                        outputBounds,
                        requiredRegion,
                        frame.DeviceBounds,
                        frame.RasterBounds.Position,
                        input.EffectiveScale,
                        _options.OutputScale,
                        output.EffectiveScale.Value,
                        _options.MaxWorkingScale,
                        _options.Intent,
                        _options.Purpose,
                        description.Kind == ShaderDescriptionKind.WholeSource
                            ? RasterShaderMapping.SemanticRasterOrigin(
                                input.Bounds,
                                input.EffectiveScale.Value,
                                input.DeviceBounds,
                                input.RasterBounds)
                            : null);
                    BindStandaloneShaderCore(
                        description,
                        context,
                        input,
                        output,
                        inputImage,
                        frame,
                        program,
                        uniforms,
                        runtimeChildren,
                        children);
                });
        }

        private static void BindStandaloneShaderCore(
            ShaderDescription description,
            ShaderExecutionContext? context,
            MaterializedRenderValue input,
            MaterializedRenderValue output,
            SKImage inputImage,
            ShaderEvaluationFrame frame,
            StandaloneShaderProgram program,
            SKRuntimeEffectUniforms uniforms,
            SKRuntimeEffectChildren runtimeChildren,
            List<SKShader> children)
        {
            SKShader inputShader = RasterShaderMapping.CreateSemanticImageShader(
                inputImage,
                input.Target.RawValue.Context,
                input.Bounds,
                input.EffectiveScale.Value,
                input.DeviceBounds,
                input.RasterBounds,
                output.EffectiveScale.Value,
                frame.RasterBounds,
                program.TileMode);
            children.Add(inputShader);
            runtimeChildren[program.ChildName] = inputShader;
            ShaderArgumentBinder.BindStandalone(description, context, uniforms, runtimeChildren, children);
        }

    }
}
