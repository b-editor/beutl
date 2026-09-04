using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Beutl.Graphics.Backend;
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
                fragment.Inputs[0].EffectiveScale.IsUnbounded ? inputRequestScale : null);
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
                    density = RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(
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

        private IReadOnlyList<MaterializedRenderValue> ExecuteCompiledShaderRun(
            CompiledShaderRun run,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
            => ExecuteOnDeviceGrid(
                currentTarget,
                () => ExecuteCompiledShaderRunCore(run, currentTarget, requestedScale));

        private IReadOnlyList<MaterializedRenderValue> ExecuteCompiledShaderRunCore(
            CompiledShaderRun run,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
        {
            RenderFragmentReference inputFragment = run.GetInput(_graph);
            RenderFragmentReference outputFragment = run.GetOutput(_graph);
            ShaderDescription? wholeSourceHead = run.GetWholeSourceHead(_graph);
            Rect outputBounds = outputFragment.Bounds;
            if (outputBounds.Width == 0 || outputBounds.Height == 0)
            {
                CompleteFragmentUse(inputFragment);
                return [];
            }

            RenderFragmentReference requirementFragment = wholeSourceHead is null
                ? outputFragment
                : run.GetStage(_graph, 0);
            Rect requiredRegion = ResolveFragmentRequirement(requirementFragment, outputBounds);
            if (requiredRegion.Width == 0 || requiredRegion.Height == 0)
            {
                CompleteFragmentUse(inputFragment);
                return [];
            }

            EffectiveScale outputRequestScale = !outputFragment.EffectiveScale.IsUnbounded
                ? outputFragment.EffectiveScale
                : requestedScale ?? EffectiveScale.At(currentTarget.Density);
            EffectiveScale inputRequestScale = requestedScale ?? outputRequestScale;
            IReadOnlyList<MaterializedRenderValue> inputs = Materialize(
                inputFragment,
                currentTarget,
                inputFragment.EffectiveScale.IsUnbounded ? inputRequestScale : null);
            if (inputs.Count == 0)
            {
                CompleteFragmentUse(inputFragment);
                return [];
            }
            if (inputs.Count != 1)
            {
                throw new InvalidOperationException(
                    "A compiled Shader run requires its declared single input to materialize exactly one value.");
            }

            MaterializedRenderValue input = inputs[0];
            float density = RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(
                outputBounds.Translate(_activeDeviceGridOffset),
                outputRequestScale.Value);
            MaterializedRenderValue output = CreateOwnedValue(
                requiredRegion,
                EffectiveScale.At(density),
                outputBounds,
                allowPreviewDrop: true,
                initializeTarget: !ShouldMaterializeForSpirv(run));
            bool succeeded = false;
            try
            {
                MaterializedRenderValue shaderInput = wholeSourceHead is null
                    ? input
                    : NormalizeSemanticShaderInput(input);
                try
                {
                    ExecuteCompiledShaderRunElement(
                        run,
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

                succeeded = true;
                return [output];
            }
            finally
            {
                if (!succeeded)
                    ReleaseUnpublished(output);
                CompleteFragmentUse(inputFragment);
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
                ? RenderScaleUtilities.ClampWorkingScaleToRasterApronBudget(
                    alignedPhysicalBounds,
                    input.EffectiveScale.Value)
                : RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(
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
                allowPreviewDrop: true);
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
            PixelRect semanticSubset;
            if (sourceRasterBounds == canonicalRasterBounds)
            {
                PixelRect semanticDeviceBounds = PixelRect.FromRect(input.Bounds, sourceScale);
                semanticSubset = new PixelRect(
                    semanticDeviceBounds.X - input.DeviceBounds.X,
                    semanticDeviceBounds.Y - input.DeviceBounds.Y,
                    semanticDeviceBounds.Width,
                    semanticDeviceBounds.Height);
            }
            else
            {
                Vector deviceGridOffset = canonicalRasterBounds.Position - sourceRasterBounds.Position;
                PixelRect semanticDeviceBounds = PixelRect.FromRect(
                    input.Bounds.Translate(deviceGridOffset),
                    sourceScale);
                semanticSubset = new PixelRect(
                    semanticDeviceBounds.X - input.DeviceBounds.X,
                    semanticDeviceBounds.Y - input.DeviceBounds.Y,
                    semanticDeviceBounds.Width,
                    semanticDeviceBounds.Height);
            }

            var imageBounds = new PixelRect(input.DeviceBounds.Size);
            return imageBounds.Contains(semanticSubset);
        }

        private void ExecuteCompiledShaderRunElement(
            CompiledShaderRun run,
            MaterializedRenderValue input,
            MaterializedRenderValue output,
            Rect outputBounds,
            Rect requiredRegion)
        {
            if (TryExecuteSpirvShaderRun(
                    run,
                    input,
                    output,
                    outputBounds,
                    requiredRegion))
            {
                return;
            }

            ExecuteCompiledShaderRunProgram(
                run,
                input,
                outputBounds,
                requiredRegion,
                output.DeviceBounds,
                output.RasterBounds,
                output.EffectiveScale.Value,
                shader => PaintOverValue(output, shader));
        }

        private bool TryExecuteSpirvShaderRun(
            CompiledShaderRun run,
            MaterializedRenderValue input,
            MaterializedRenderValue output,
            Rect outputBounds,
            Rect requiredRegion)
        {
            if (_shaderBackendPreference == ShaderBackendPreference.Sksl)
                return false;

            ShaderDescription? stageDescription = run.StageFragmentIndices.Length == 1
                ? run.GetDescription(_graph, 0)
                : null;
            SpirvShaderLowering? lowering = stageDescription?.SpirvLowering;
            if (lowering is null)
            {
                if (_shaderBackendPreference == ShaderBackendPreference.Spirv)
                {
                    throw new InvalidOperationException(
                        "The compiled shader run cannot be lowered to the requested SPIR-V backend.");
                }
                return false;
            }
            IGraphicsContext? graphicsContext = GraphicsContextFactory.SharedContext;
            ITexture2D? sourceTexture = input.Target.Texture;
            ITexture2D? destinationTexture = output.Target.Texture;
            bool compatible = SpirvShaderProgramCache.SupportsExecution(graphicsContext)
                              && sourceTexture is not null
                              && destinationTexture is not null
                              && sourceTexture.Format == TextureFormat.RGBA16Float
                              && destinationTexture.Format == TextureFormat.RGBA16Float
                              && input.EffectiveScale == output.EffectiveScale
                              && input.DeviceBounds.Intersect(output.DeviceBounds) == output.DeviceBounds
                              && input.Bounds == outputBounds
                              && output.Bounds == requiredRegion;
            if (!compatible)
            {
                if (_shaderBackendPreference == ShaderBackendPreference.Spirv)
                {
                    throw new InvalidOperationException(
                        "The requested SPIR-V backend requires the engine Vulkan recording context and matching "
                        + "RGBA16F input and output footprints. "
                        + $"Source format/footprint: {sourceTexture?.Format} {input.DeviceBounds} {input.RasterBounds} {input.Bounds}; "
                        + $"destination format/footprint: {destinationTexture?.Format} {output.DeviceBounds} {output.RasterBounds} {output.Bounds}; "
                        + $"complete output/requirement: {outputBounds} {requiredRegion}.");
                }
                return false;
            }

            ProgramCacheContextKey contextKey =
                SpirvShaderProgramCache.CreateContextKey(_programCacheContext);
            ProgramCacheLease<GLSLFilterPipeline> lease;
            try
            {
                lease = SpirvShaderProgramCache.Acquire(
                    _spirvProgramCache,
                    stageDescription!,
                    graphicsContext!,
                    contextKey);
            }
            catch (InvalidOperationException) when (_shaderBackendPreference == ShaderBackendPreference.Auto)
            {
                // The SkSL lowering is the compatibility contract. A native compile/resource failure must not
                // change existing output, and the absent cache entry lets a later execution retry SPIR-V.
                return false;
            }
            using (lease)
            {
                RenderExecutionSessionToken bindingToken = CreateExecutionSessionToken();
                SpirvPushConstants pushConstants = default;
                PixelPoint sourceTexelOffset = output.DeviceBounds.Position - input.DeviceBounds.Position;
                bindingToken.RunAndComplete(
                    () =>
                    {
                        ShaderExecutionContext context = CreateCompiledShaderStageContext(
                            run,
                            stageIndex: 0,
                            bindingToken,
                            input,
                            outputBounds,
                            requiredRegion,
                            output.DeviceBounds,
                            output.RasterBounds,
                            output.EffectiveScale.Value);
                        pushConstants = lowering.Bind(
                            stageDescription!,
                            context,
                            sourceTexelOffset);
                    });

                input.Target.PrepareForSampling(RenderTargetSamplingIntent.BackendInterop);
                lease.Program.Execute(sourceTexture!, destinationTexture!, pushConstants);
                lease.Program.SubmitPendingCommands();

                _shaderRunExecutions++;
                _shaderStageExecutions++;
                _spirvShaderRunExecutions++;
                if (lease.IsCacheHit)
                    _programCacheHits++;
            }
            return true;
        }

        private bool ShouldMaterializeForSpirv(CompiledShaderRun run)
        {
            if (_shaderBackendPreference == ShaderBackendPreference.Sksl
                || run.StageFragmentIndices.Length != 1
                || run.GetDescription(_graph, 0).SpirvLowering is null)
            {
                return false;
            }

            return _shaderBackendPreference == ShaderBackendPreference.Spirv
                   || SpirvShaderProgramCache.SupportsExecution(GraphicsContextFactory.SharedContext);
        }

        private bool ShouldDeferDirectReplayToSpirv(CompiledShaderRun run)
            => ShouldMaterializeForSpirv(run)
               && (_shaderBackendPreference == ShaderBackendPreference.Spirv
                   || run.GetInput(_graph).HasOpaqueExternalWork);

        private void ExecuteCompiledShaderRunProgram(
            CompiledShaderRun run,
            MaterializedRenderValue input,
            Rect outputBounds,
            Rect requiredRegion,
            PixelRect outputDeviceBounds,
            Rect outputRasterBounds,
            float outputScale,
            Action<SKShader> draw)
        {
            ShaderDescription? wholeSourceHead = run.GetWholeSourceHead(_graph);
            RenderFragmentReference inputFragment = run.GetInput(_graph);
            ShaderEvaluationFrame frame = wholeSourceHead is null
                ? ShaderEvaluationFrame.Destination(outputDeviceBounds, outputRasterBounds)
                : RasterShaderMapping.CreateWholeSourceFrame(
                    outputBounds,
                    outputDeviceBounds,
                    outputRasterBounds,
                    outputScale);
            using SKImage inputImage = input.Target.Value.Snapshot();
            ProgramCacheContextKey contextKey = CreateProgramContextKey(run.Program.Budget);
            using ProgramCacheLease<CachedSkRuntimeEffect> lease = AcquireProgram(run, contextKey);
            using var uniforms = new SKRuntimeEffectUniforms(lease.Program.Effect);
            using var runtimeChildren = new SKRuntimeEffectChildren(lease.Program.Effect);
            var children = new List<SKShader>();
            RenderExecutionSessionToken bindingToken = CreateExecutionSessionToken();
            try
            {
                bindingToken.RunAndComplete(
                    () =>
                    {
                        SKShader inputShader;
                        if (wholeSourceHead is { } head)
                        {
                            inputShader = RasterShaderMapping.CreateSemanticImageShader(
                                inputImage,
                                input.Target.RawValue.Context,
                                input.Bounds,
                                input.EffectiveScale.Value,
                                input.DeviceBounds,
                                input.RasterBounds,
                                outputScale,
                                frame.RasterBounds,
                                head.SourceTileMode);
                        }
                        else
                        {
                            bool interpolatedBitmap = inputFragment.Kind == RenderFragmentKind.OpaqueSource
                                && ((OpaqueRenderFragmentPayload)inputFragment.Payload!).Description
                                    .DirectReplayAtExactIntegerReduction;
                            SKSamplingOptions sampling = interpolatedBitmap
                                ? RasterShaderMapping.SamplingFor(
                                        input.EffectiveScale.Value,
                                        outputScale)
                                : SKSamplingOptions.Default;
                            SKShaderTileMode tileMode = interpolatedBitmap
                                ? SKShaderTileMode.Clamp
                                : SKShaderTileMode.Decal;
                            inputShader = inputImage.ToShader(
                                tileMode,
                                tileMode,
                                sampling,
                                RasterShaderMapping.CreateLocalMatrix(
                                    outputScale,
                                    input.EffectiveScale.Value,
                                    outputRasterBounds,
                                    input.RasterBounds));
                        }
                        children.Add(inputShader);
                        runtimeChildren[SkslSnippetMerger.SourceChildName] = inputShader;

                        var descriptionsByMergedIndex = new Dictionary<int, ShaderDescription>();
                        var contextsByMergedIndex = new Dictionary<int, ShaderExecutionContext>();
                        for (int index = 0; index < run.Program.Stages.Count; index++)
                        {
                            int mergedIndex = run.Program.Stages[index].StageIndex;
                            descriptionsByMergedIndex.Add(
                                mergedIndex,
                                run.GetDescription(_graph, index));
                            contextsByMergedIndex.Add(
                                mergedIndex,
                                CreateCompiledShaderStageContext(
                                    run,
                                    index,
                                    bindingToken,
                                    input,
                                    outputBounds,
                                    requiredRegion,
                                    outputDeviceBounds,
                                    outputRasterBounds,
                                    outputScale));
                        }

                        foreach (SkslMergedBindingLayout layout in run.Program.Bindings)
                        {
                            ShaderExecutionContext context = contextsByMergedIndex[layout.StageIndex];
                            ShaderDescription description = descriptionsByMergedIndex[layout.StageIndex];
                            if (layout.Kind == SkslBindingKind.Uniform)
                            {
                                ShaderUniformBinding binding = description.Uniforms[layout.BindingIndex];
                                SkslUniformDeclaration declaration = description.Source.Uniforms[binding.Name];
                                ShaderUniformValue value = binding.Bind(declaration, context);
                                SkslUniformAssignment.SetUniform(
                                    uniforms,
                                    layout.MergedName,
                                    declaration,
                                    value);
                            }
                            else
                            {
                                ShaderResourceBinding binding = description.Resources[layout.BindingIndex];
                                SKShader child = binding.Bind(context);
                                children.Add(child);
                                runtimeChildren[layout.MergedName] = child;
                            }
                        }
                    });

                using SKShader shader = lease.Program.Effect.ToShader(uniforms, runtimeChildren);
                DrawInEvaluationFrame(shader, frame, draw);

                _shaderRunExecutions++;
                _shaderStageExecutions = checked(_shaderStageExecutions + run.StageFragmentIndices.Length);
                if (run.IsFused)
                    _fusedShaderRunExecutions++;
                if (lease.IsCacheHit)
                    _programCacheHits++;
            }
            finally
            {
                // Reverse index walk: the LINQ form buffers the whole list before yielding, and this runs
                // in a per-frame teardown path.
                for (int index = children.Count - 1; index >= 0; index--)
                    children[index].Dispose();
            }
        }

        // Only valid while the run's source child is mapped against the same frame's raster bounds; the two
        // shifts cancel, so the program keeps sampling the texel the destination pixel already resolved to.
        private static void DrawInEvaluationFrame(
            SKShader shader,
            ShaderEvaluationFrame frame,
            Action<SKShader> draw)
        {
            if (frame.FragmentOrigin == default)
            {
                draw(shader);
                return;
            }

            using SKShader rebased = shader.WithLocalMatrix(
                SKMatrix.CreateTranslation(-frame.FragmentOrigin.X, -frame.FragmentOrigin.Y));
            draw(rebased);
        }

        private ShaderExecutionContext CreateCompiledShaderStageContext(
            CompiledShaderRun run,
            int stageIndex,
            RenderExecutionSessionToken bindingToken,
            MaterializedRenderValue runInput,
            Rect runOutputBounds,
            Rect runRequiredRegion,
            PixelRect runOutputDeviceBounds,
            Rect runOutputRasterBounds,
            float runWorkingScale)
        {
            bool isFirst = stageIndex == 0;
            bool isLast = stageIndex == run.StageFragmentIndices.Length - 1;
            RenderFragmentReference fragment = run.GetStage(_graph, stageIndex);
            ShaderDescription description = run.GetDescription(_graph, stageIndex);
            RenderFragmentReference fragmentInput = fragment.Inputs.Single();
            Rect inputBounds = isFirst
                ? runInput.Bounds
                : ResolveFragmentRequirement(fragmentInput, fragmentInput.Bounds);
            Rect outputBounds = isLast ? runOutputBounds : fragment.Bounds;
            Rect requiredRegion = isLast
                ? runRequiredRegion
                : ResolveFragmentRequirement(fragment, fragment.Bounds);
            EffectiveScale inputEffectiveScale = isFirst
                ? runInput.EffectiveScale
                : EffectiveScale.At(runWorkingScale);
            float workingScale = runWorkingScale;
            Vector deviceGridOffset = new(
                (runOutputDeviceBounds.X / workingScale) - runOutputRasterBounds.X,
                (runOutputDeviceBounds.Y / workingScale) - runOutputRasterBounds.Y);
            PixelRect deviceBounds;
            if (description.Kind == ShaderDescriptionKind.WholeSource)
            {
                deviceBounds = RasterShaderMapping.CreateWholeSourceFrame(
                        outputBounds,
                        runOutputDeviceBounds,
                        runOutputRasterBounds,
                        workingScale)
                    .DeviceBounds;
            }
            else
            {
                deviceBounds = isLast
                    ? runOutputDeviceBounds
                    : PixelRect.FromRect(
                        requiredRegion.Translate(deviceGridOffset),
                        workingScale);
            }

            Rect rasterBounds = deviceBounds
                .ToRect(workingScale)
                .Translate(-deviceGridOffset);
            return new ShaderExecutionContext(
                bindingToken,
                inputBounds,
                outputBounds,
                requiredRegion,
                deviceBounds,
                rasterBounds,
                inputEffectiveScale,
                _options.OutputScale,
                workingScale,
                _options.MaxWorkingScale,
                _options.Intent,
                _options.Purpose);
        }

        private ProgramCacheLease<CachedSkRuntimeEffect> AcquireProgram(
            CompiledShaderRun run,
            ProgramCacheContextKey contextKey)
        {
            return _programCache.GetOrCreate(
                run.Program,
                contextKey,
                CachedSkRuntimeEffect.Create);
        }

        private ProgramCacheContextKey CreateProgramContextKey(SkslBackendBudget budget)
            => SkRuntimeEffectProgramCache.CreateContextKey(_programCacheContext, budget);

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
            RenderExecutionSessionToken bindingToken = CreateExecutionSessionToken();
            try
            {
                bindingToken.RunAndComplete(
                    () =>
                    {
                        var context = new ShaderExecutionContext(
                            bindingToken,
                            input.Bounds,
                            outputBounds,
                            requiredRegion,
                            frame.DeviceBounds,
                            frame.RasterBounds,
                            input.EffectiveScale,
                            _options.OutputScale,
                            output.EffectiveScale.Value,
                            _options.MaxWorkingScale,
                            _options.Intent,
                            _options.Purpose);
                        foreach (ShaderUniformBinding binding in description.Uniforms)
                        {
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
                            tileMode);
                        children.Add(inputShader);
                        runtimeChildren[childName] = inputShader;

                        foreach (ShaderResourceBinding binding in description.Resources)
                        {
                            SKShader child = binding.Bind(context);
                            children.Add(child);
                            runtimeChildren[binding.Name] = child;
                        }
                    });

                using SKShader shader = lease.Program.Effect.ToShader(uniforms, runtimeChildren);
                DrawInEvaluationFrame(
                    shader,
                    frame,
                    rebased => PaintOverValue(output, rebased));
            }
            finally
            {
                // Reverse index walk: the LINQ form buffers the whole list before yielding, and this runs
                // in a per-frame teardown path.
                for (int index = children.Count - 1; index >= 0; index--)
                    children[index].Dispose();
            }
        }

    }
}
