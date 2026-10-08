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
                        allowPreviewDrop: true,
                        pixelFormat: RenderTargetPixelFormat.LinearPremultipliedRgba16Float);
                    bool succeeded = false;
                    try
                    {
                        // Sampling converts the sRGB composition raster into the linear output, so the
                        // input needs no linear copy of its own.
                        MaterializedRenderValue shaderInput = NormalizeSemanticShaderInput(input);
                        try
                        {
                            ExecuteShaderElement(
                                description,
                                shaderInput,
                                output,
                                outputBounds,
                                requiredRegion);
                            // The program has released its input snapshot; drop the cached one before the flush.
                            SurfaceSnapshot.Release(shaderInput.Target.Value);
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

            MaterializedRenderValue normalized = CreateNormalizedSemanticShaderInput(
                input,
                addRasterApron: false);
            if (CanUseAsSemanticShaderInput(normalized))
                return normalized;

            // Global device-grid alignment can move a locally exact edge by a sub-pixel epsilon.
            // Keep the effect-item exact placement whenever it is sufficient, and add an apron only for
            // the residual case where raster-local pixel rounding still falls outside the image.
            ReleaseUnpublished(normalized);
            return CreateNormalizedSemanticShaderInput(input, addRasterApron: true);
        }

        private MaterializedRenderValue CreateNormalizedSemanticShaderInput(
            MaterializedRenderValue input,
            bool addRasterApron)
        {
            Rect physicalBounds = input.RasterBounds.Union(input.Bounds);
            Rect alignedPhysicalBounds = physicalBounds.Translate(input.DeviceGridOffset);
            float density = addRasterApron
                ? BufferDimensionBudget.EngineCeiling.ClampWorkingScaleToRasterApron(
                    alignedPhysicalBounds,
                    input.EffectiveScale.Value)
                : BufferDimensionBudget.EngineCeiling.ClampWorkingScaleToExactFootprint(
                    alignedPhysicalBounds,
                    input.EffectiveScale.Value);
            EffectiveScale normalizedScale = EffectiveScale.At(density);
            PixelRect normalizedDeviceBounds = PixelRect.FromRect(physicalBounds, density);
            if (addRasterApron)
                normalizedDeviceBounds = RenderScaleUtilities.AddRasterApron(normalizedDeviceBounds);
            MaterializedRenderValue normalized = CreateOwnedValue(
                input.Bounds,
                normalizedScale,
                input.CompleteBounds,
                physicalDeviceBounds: normalizedDeviceBounds,
                deviceGridOffset: input.DeviceGridOffset,
                allowPreviewDrop: true,
                pixelFormat: input.Target.ColorSpace == BitmapColorSpace.LinearSrgb
                    ? RenderTargetPixelFormat.LinearPremultipliedRgba16Float
                    : RenderTargetPixelFormat.SrgbPremultipliedRgba16Float);
            bool succeeded = false;
            try
            {
                using var canvas = CreateValueCanvas(normalized);
                using (canvas.PushTransform(normalized.RasterAlignmentTransform))
                {
                    canvas.DrawRenderTargetScaledWithoutFlush(input.Target, input.RasterBounds);
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
                    "A effectItem shader program requires a materialized execution destination.");
            return AcquireStandaloneProgram(renderTarget, source);
        }

        private ProgramCacheLease<CachedSkRuntimeEffect> AcquireStandaloneProgram(
            RenderTarget target,
            string source)
        {
            ProgramCacheLease<CachedSkRuntimeEffect> lease =
                SkRuntimeEffectProgramCache.AcquireForDestination(
                    _programCache,
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
            using SKImage inputImage = input.Target.Value.Snapshot();
            ShaderEvaluationFrame frame = description.Kind == ShaderDescriptionKind.WholeSource
                ? RasterShaderMapping.CreateWholeSourceFrame(
                    outputBounds,
                    output.DeviceBounds,
                    output.RasterBounds,
                    output.EffectiveScale.Value)
                : ShaderEvaluationFrame.Destination(output.DeviceBounds, output.RasterBounds);
            string childName;
            string programSource;
            SKShaderTileMode tileMode;
            if (description.Kind == ShaderDescriptionKind.CurrentPixel)
            {
                childName = "__beutl_src";
                tileMode = SKShaderTileMode.Decal;
                programSource = $"uniform shader {childName};\n{description.Source.Text}\n"
                    + $"half4 main(float2 __beutl_coord) {{ return apply({childName}.eval(__beutl_coord)); }}\n";
            }
            else
            {
                childName = "src";
                tileMode = description.SourceTileMode;
                programSource = description.Source.Text;
            }

            using ProgramCacheLease<CachedSkRuntimeEffect> lease =
                AcquireStandaloneProgram(output.Target, programSource);
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
                        childName,
                        tileMode,
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
                        childName,
                        tileMode,
                        uniforms,
                        runtimeChildren,
                        children);
                }

                using SKShader shader = lease.Program.Effect.ToShader(uniforms, runtimeChildren);
                PaintInEvaluationFrame(output, shader, frame);
            }
            finally
            {
                // Reverse index walk: the LINQ form buffers the whole list before yielding, and this runs
                // in a per-frame teardown path.
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
            string childName,
            SKShaderTileMode tileMode,
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
                        _options.Purpose);
                    BindStandaloneShaderCore(
                        description,
                        context,
                        input,
                        output,
                        inputImage,
                        frame,
                        childName,
                        tileMode,
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
            string childName,
            SKShaderTileMode tileMode,
            SKRuntimeEffectUniforms uniforms,
            SKRuntimeEffectChildren runtimeChildren,
            List<SKShader> children)
        {
            for (int index = 0; index < description.Uniforms.Count; index++)
            {
                ShaderUniformBinding binding = description.Uniforms[index];
                if (!description.Source.Uniforms.TryGetValue(
                        binding.Name,
                        out SkslUniformDeclaration declaration))
                {
                    throw new InvalidOperationException(
                        $"Shader uniform '{binding.Name}' was not declared.");
                }

                ShaderUniformValue value = binding.Bind(declaration, context);
                SkslUniformAssignment.SetUniform(
                    uniforms,
                    binding.Name,
                    declaration,
                    value);
            }

            SKShader inputShader = RasterShaderMapping.CreateSemanticImageShader(
                inputImage,
                input.Target.RawValue.Context,
                input.Bounds,
                input.EffectiveScale.Value,
                input.DeviceBounds,
                input.RasterBounds,
                output.EffectiveScale.Value,
                frame.RasterBounds,
                tileMode,
                output.Target.ColorSpace);
            children.Add(inputShader);
            runtimeChildren[childName] = inputShader;

            for (int index = 0; index < description.Resources.Count; index++)
            {
                ShaderResourceBinding binding = description.Resources[index];
                SKShader child = binding.Bind(context
                    ?? throw new InvalidOperationException(
                        "A shader resource binding requires an execution context."));
                children.Add(child);
                runtimeChildren[binding.Name] = child;
            }
        }

    }
}
