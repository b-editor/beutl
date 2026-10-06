using Beutl.Graphics.Shaders;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RenderRequestExecutor
{
    private sealed partial class RenderRequestExecutionState
    {
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
            EffectiveScale inputRequestScale = ResolveShaderRunInputCallerScale(
                run,
                requestedScale ?? outputRequestScale);
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
            float density = BufferDimensionBudget.EngineCeiling.ClampWorkingScaleToExactFootprint(
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
                ShaderRunDestination.ForMaterialized(output),
                outputBounds,
                requiredRegion);
        }

        private void ExecuteCompiledShaderRunProgram(
            CompiledShaderRun run,
            MaterializedRenderValue input,
            ShaderRunDestination destination,
            Rect outputBounds,
            Rect requiredRegion)
        {
            ShaderDescription? wholeSourceHead = run.GetWholeSourceHead(_graph);
            RenderFragmentReference inputFragment = run.GetInput(_graph);
            PixelRect outputDeviceBounds = destination.DeviceBounds;
            Rect outputRasterBounds = destination.RasterBounds;
            float outputScale = destination.Scale;
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
            try
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
                        ? RasterShaderMapping.SamplingFor(input.EffectiveScale.Value, outputScale)
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

                if (HasExecutionContextBinders(run))
                {
                    BindCompiledShaderBindingsWithCallbacks(
                        run,
                        input,
                        outputBounds,
                        requiredRegion,
                        outputDeviceBounds,
                        outputRasterBounds,
                        outputScale,
                        uniforms,
                        runtimeChildren,
                        children);
                }
                else
                {
                    BindCompiledShaderBindingsCore(
                        run,
                        contexts: null,
                        uniforms,
                        runtimeChildren,
                        children);
                }

                using SKShader shader = lease.Program.Effect.ToShader(uniforms, runtimeChildren);
                PaintInEvaluationFrame(destination, shader, frame);

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

        private bool HasExecutionContextBinders(CompiledShaderRun run)
        {
            for (int index = 0; index < run.Program.StageCount; index++)
            {
                if (run.GetDescription(_graph, index).HasExecutionContextBinder)
                    return true;
            }
            return false;
        }

        private void BindCompiledShaderBindingsWithCallbacks(
            CompiledShaderRun run,
            MaterializedRenderValue input,
            Rect outputBounds,
            Rect requiredRegion,
            PixelRect outputDeviceBounds,
            Rect outputRasterBounds,
            float outputScale,
            SKRuntimeEffectUniforms uniforms,
            SKRuntimeEffectChildren runtimeChildren,
            List<SKShader> children)
        {
            RenderExecutionSessionToken bindingToken = CreateExecutionSessionToken();
            bindingToken.RunAndComplete(
                () =>
                {
                    var contexts = new ShaderExecutionContext?[run.Program.StageCount];
                    for (int index = 0; index < contexts.Length; index++)
                    {
                        if (!run.GetDescription(_graph, index).HasExecutionContextBinder)
                            continue;
                        contexts[index] = CreateCompiledShaderStageContext(
                            run,
                            index,
                            bindingToken,
                            input,
                            outputBounds,
                            requiredRegion,
                            outputDeviceBounds,
                            outputRasterBounds,
                            outputScale);
                    }

                    BindCompiledShaderBindingsCore(
                        run,
                        contexts,
                        uniforms,
                        runtimeChildren,
                        children);
                });
        }

        private void BindCompiledShaderBindingsCore(
            CompiledShaderRun run,
            ShaderExecutionContext?[]? contexts,
            SKRuntimeEffectUniforms uniforms,
            SKRuntimeEffectChildren runtimeChildren,
            List<SKShader> children)
        {
            foreach (ref readonly SkslMergedBindingLayout layout in run.Program.Bindings.AsSpan())
            {
                int localIndex = layout.StageIndex;
                if ((uint)localIndex >= (uint)run.Program.StageCount)
                {
                    throw new InvalidOperationException(
                        "A merged shader binding references a non-canonical stage index.");
                }

                ShaderDescription description = run.GetDescription(_graph, localIndex);
                ShaderExecutionContext? context = contexts?[localIndex];
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
                    SKShader child = binding.Bind(context
                        ?? throw new InvalidOperationException(
                            "A shader resource binding requires an execution context."));
                    children.Add(child);
                    runtimeChildren[layout.MergedName] = child;
                }
            }
        }

        // Only valid while the run's source child is mapped against the same frame's raster bounds; the two
        // shifts cancel, so the program keeps sampling the texel the destination pixel already resolved to.
        private void PaintInEvaluationFrame(
            ShaderRunDestination destination,
            SKShader shader,
            ShaderEvaluationFrame frame)
        {
            PixelPoint fragmentOrigin = destination.DeviceBounds.Position - frame.DeviceBounds.Position;
            if (fragmentOrigin == default)
            {
                PaintInEvaluationFrameCore(destination, shader);
                return;
            }

            using SKShader rebased = shader.WithLocalMatrix(
                SKMatrix.CreateTranslation(-fragmentOrigin.X, -fragmentOrigin.Y));
            PaintInEvaluationFrameCore(destination, rebased);
        }

        private void PaintInEvaluationFrameCore(ShaderRunDestination destination, SKShader shader)
        {
            if (destination.MaterializedOutput is { } output)
            {
                PaintOverValue(output, shader);
                return;
            }

            DirectShaderRunPlan directPlan = destination.DirectPlan;
            using SKShader mapped = shader.WithLocalMatrix(
                SKMatrix.CreateScaleTranslation(
                    1f / directPlan.Density,
                    1f / directPlan.Density,
                    directPlan.OutputDeviceBounds.X / directPlan.Density,
                    directPlan.OutputDeviceBounds.Y / directPlan.Density));
            using var paint = new SKPaint
            {
                Shader = mapped,
                IsAntialias = false,
            };
            ImmediateCanvas canvas = destination.DirectCanvas
                ?? throw new InvalidOperationException("A direct shader destination requires a canvas.");
            canvas.VerifyAccess();
            canvas.Canvas.DrawRect(directPlan.RasterBounds.ToSKRect(), paint);
        }

        private void PaintInEvaluationFrame(
            MaterializedRenderValue output,
            SKShader shader,
            ShaderEvaluationFrame frame)
            => PaintInEvaluationFrame(ShaderRunDestination.ForMaterialized(output), shader, frame);

        private readonly struct ShaderRunDestination
        {
            private ShaderRunDestination(
                MaterializedRenderValue? materializedOutput,
                ImmediateCanvas? directCanvas,
                DirectShaderRunPlan directPlan)
            {
                MaterializedOutput = materializedOutput;
                DirectCanvas = directCanvas;
                DirectPlan = directPlan;
            }

            public MaterializedRenderValue? MaterializedOutput { get; }

            public ImmediateCanvas? DirectCanvas { get; }

            public DirectShaderRunPlan DirectPlan { get; }

            public PixelRect DeviceBounds => MaterializedOutput?.DeviceBounds ?? DirectPlan.OutputDeviceBounds;

            public Rect RasterBounds => MaterializedOutput?.RasterBounds ?? DirectPlan.RasterBounds;

            public float Scale => MaterializedOutput?.EffectiveScale.Value ?? DirectPlan.Density;

            public static ShaderRunDestination ForMaterialized(MaterializedRenderValue output)
            {
                ArgumentNullException.ThrowIfNull(output);
                return new ShaderRunDestination(output, null, default);
            }

            public static ShaderRunDestination ForDirect(
                ImmediateCanvas canvas,
                DirectShaderRunPlan directPlan)
            {
                ArgumentNullException.ThrowIfNull(canvas);
                return new ShaderRunDestination(null, canvas, directPlan);
            }
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
                rasterBounds.Position,
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
    }
}
