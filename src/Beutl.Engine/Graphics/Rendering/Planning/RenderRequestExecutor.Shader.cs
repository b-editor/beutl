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
        private IReadOnlyList<CompatibilityRenderValue> ExecuteShader(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
            => ExecuteOnDeviceGrid(
                currentTarget,
                () => ExecuteShaderCore(fragment, currentTarget, requestedScale));

        private IReadOnlyList<CompatibilityRenderValue> ExecuteShaderCore(
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
            IReadOnlyList<CompatibilityRenderValue> inputs = Materialize(
                fragment.Inputs[0],
                currentTarget,
                fragment.Inputs[0].EffectiveScale.IsUnbounded ? inputRequestScale : null);
            var results = new List<CompatibilityRenderValue>(inputs.Count);
            bool executed = false;
            try
            {
                foreach (CompatibilityRenderValue input in inputs)
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
                    CompatibilityRenderValue output = CreateOwnedValue(
                        requiredRegion,
                        outputScale,
                        outputBounds,
                        allowPreviewDrop: true);
                    bool succeeded = false;
                    try
                    {
                        ExecuteShaderElement(
                            fragment.Id?.Value ?? 0,
                            description,
                            input,
                            output,
                            outputBounds,
                            requiredRegion);
                        executed = true;
                        results.Add(output);
                        succeeded = true;
                    }
                    finally
                    {
                        if (!succeeded)
                            ReleaseUnpublished(output);
                    }
                }

                if (!executed)
                    MarkExecutionSkipped(fragment);
                return results;
            }
            catch
            {
                foreach (CompatibilityRenderValue value in results)
                    ReleaseUnpublished(value);
                throw;
            }
            finally
            {
                CompleteFragmentUse(fragment.Inputs[0]);
            }
        }

        private IReadOnlyList<CompatibilityRenderValue> ExecuteCompiledShaderRun(
            CompiledShaderRun run,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
            => ExecuteOnDeviceGrid(
                currentTarget,
                () => ExecuteCompiledShaderRunCore(run, currentTarget, requestedScale));

        private IReadOnlyList<CompatibilityRenderValue> ExecuteCompiledShaderRunCore(
            CompiledShaderRun run,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
        {
            Rect outputBounds = run.Output.Bounds;
            if (outputBounds.Width == 0 || outputBounds.Height == 0)
            {
                CompleteFragmentUse(run.Input);
                MarkExecutionSkipped(run.Output);
                return [];
            }

            Rect requiredRegion = ResolveFragmentRequirement(run.Output, outputBounds);
            if (requiredRegion.Width == 0 || requiredRegion.Height == 0)
            {
                CompleteFragmentUse(run.Input);
                MarkExecutionSkipped(run.Output);
                return [];
            }

            EffectiveScale outputRequestScale = !run.Output.EffectiveScale.IsUnbounded
                ? run.Output.EffectiveScale
                : requestedScale ?? EffectiveScale.At(currentTarget.Density);
            EffectiveScale inputRequestScale = requestedScale ?? outputRequestScale;
            IReadOnlyList<CompatibilityRenderValue> inputs = Materialize(
                run.Input,
                currentTarget,
                run.Input.EffectiveScale.IsUnbounded ? inputRequestScale : null);
            if (inputs.Count == 0)
            {
                CompleteFragmentUse(run.Input);
                MarkExecutionSkipped(run.Output);
                return [];
            }
            if (inputs.Count != 1)
            {
                throw new InvalidOperationException(
                    "A compiled Shader run requires its declared single input to materialize exactly one value.");
            }

            CompatibilityRenderValue input = inputs[0];
            float density = RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(
                outputBounds.Translate(_activeDeviceGridOffset),
                outputRequestScale.Value);
            CompatibilityRenderValue output = CreateOwnedValue(
                requiredRegion,
                EffectiveScale.At(density),
                outputBounds,
                allowPreviewDrop: true);
            bool succeeded = false;
            try
            {
                ExecuteCompiledShaderRunElement(
                    run,
                    input,
                    output,
                    outputBounds,
                    requiredRegion);
                succeeded = true;
                return [output];
            }
            finally
            {
                if (!succeeded)
                    ReleaseUnpublished(output);
                CompleteFragmentUse(run.Input);
            }
        }

        private void ExecuteCompiledShaderRunElement(
            CompiledShaderRun run,
            CompatibilityRenderValue input,
            CompatibilityRenderValue output,
            Rect outputBounds,
            Rect requiredRegion)
            => ExecuteCompiledShaderRunProgram(
                run,
                input,
                outputBounds,
                requiredRegion,
                output.DeviceBounds,
                output.RasterBounds,
                output.EffectiveScale.Value,
                shader =>
                {
                    using var paint = new SKPaint { Shader = shader };
                    using var canvas = ImmediateCanvas.CreateExecutorManaged(
                        output.Target,
                        output.EffectiveScale.Value,
                        _options.MaxWorkingScale,
                        output.RasterBounds.Size,
                        _options.Intent,
                        output.DeviceBounds.Position);
                    canvas.Clear();
                    using (canvas.PushDeviceSpace())
                    {
                        canvas.Canvas.DrawRect(
                            SKRect.Create(output.Target.Width, output.Target.Height),
                            paint);
                    }
                });

        private void ExecuteCompiledShaderRunProgram(
            CompiledShaderRun run,
            CompatibilityRenderValue input,
            Rect outputBounds,
            Rect requiredRegion,
            PixelRect outputDeviceBounds,
            Rect outputRasterBounds,
            float outputScale,
            Action<SKShader> draw)
        {
            using SKImage inputImage = input.Target.Value.Snapshot();
            ProgramCacheContextKey contextKey = CreateProgramContextKey(run.Program.Budget);
            using ProgramCacheLease<CachedSkRuntimeEffect> lease = AcquireProgram(run, contextKey);
            using var uniforms = new SKRuntimeEffectUniforms(lease.Program.Effect);
            using var runtimeChildren = new SKRuntimeEffectChildren(lease.Program.Effect);
            var children = new List<SKShader>();
            var bindingToken = new RenderExecutionSessionToken();
            try
            {
                try
                {
                    bindingToken.RunAndComplete(
                        () =>
                        {
                            SKShader inputShader = inputImage.ToShader(
                                SKShaderTileMode.Decal,
                                SKShaderTileMode.Decal,
                                RasterShaderMapping.CreateLocalMatrix(
                                    outputScale,
                                    input.EffectiveScale.Value,
                                    outputRasterBounds,
                                    input.RasterBounds));
                            children.Add(inputShader);
                            runtimeChildren[SkslSnippetMerger.SourceChildName] = inputShader;

                            var stagesByMergedIndex = new Dictionary<int, CompiledShaderStage>();
                            var contextsByMergedIndex = new Dictionary<int, ShaderExecutionContext>();
                            for (int index = 0; index < run.Program.Stages.Count; index++)
                            {
                                int mergedIndex = run.Program.Stages[index].StageIndex;
                                CompiledShaderStage stage = run.Stages[index];
                                stagesByMergedIndex.Add(mergedIndex, stage);
                                contextsByMergedIndex.Add(
                                    mergedIndex,
                                    CreateCompiledShaderStageContext(
                                        run,
                                        stage,
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
                                CompiledShaderStage stage = stagesByMergedIndex[layout.StageIndex];
                                ShaderExecutionContext context = contextsByMergedIndex[layout.StageIndex];
                                ShaderDescription description = stage.Description;
                                if (layout.Kind == SkslBindingKind.Uniform)
                                {
                                    ShaderUniformBinding binding = description.Uniforms[layout.BindingIndex];
                                    SkslUniformDeclaration declaration = description.Source.Uniforms[binding.Name];
                                    ShaderUniformValue value = binding.Bind(declaration, context);
                                    SetUniform(uniforms, layout.MergedName, declaration, value);
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
                }
                catch
                {
                    RecordFailure(
                        RenderPipelineFailurePhase.Binding,
                        run.Output.Id?.Value);
                    throw;
                }

                using SKShader shader = lease.Program.Effect.ToShader(uniforms, runtimeChildren);
                draw(shader);

                _shaderRunExecutions++;
                _shaderStageExecutions = checked(_shaderStageExecutions + run.Stages.Length);
                if (run.IsFused)
                    _fusedShaderRunExecutions++;
                if (lease.IsCacheHit)
                    _programCacheHits++;
                _diagnostics?.RecordGpuPassExecuted(run.Output.Id?.Value ?? 0);
            }
            finally
            {
                foreach (SKShader child in children.AsEnumerable().Reverse())
                    child.Dispose();
            }
        }

        private ShaderExecutionContext CreateCompiledShaderStageContext(
            CompiledShaderRun run,
            CompiledShaderStage stage,
            int stageIndex,
            RenderExecutionSessionToken bindingToken,
            CompatibilityRenderValue runInput,
            Rect runOutputBounds,
            Rect runRequiredRegion,
            PixelRect runOutputDeviceBounds,
            Rect runOutputRasterBounds,
            float runWorkingScale)
        {
            bool isFirst = stageIndex == 0;
            bool isLast = stageIndex == run.Stages.Length - 1;
            RenderFragmentReference fragment = stage.Fragment;
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
            PixelRect deviceBounds = isLast
                ? runOutputDeviceBounds
                : PixelRect.FromRect(
                    requiredRegion.Translate(deviceGridOffset),
                    workingScale);
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
            try
            {
                ProgramCacheLease<CachedSkRuntimeEffect> lease = _programCache.GetOrCreate(
                    run.Program,
                    contextKey,
                    CachedSkRuntimeEffect.Create);
                _diagnostics?.RecordProgramCacheDecision(
                    run.Output.Id?.Value ?? 0,
                    lease.IsCacheHit);
                return lease;
            }
            catch
            {
                RecordFailure(
                    RenderPipelineFailurePhase.ProgramCompilation,
                    run.Output.Id?.Value);
                throw;
            }
        }

        private ProgramCacheContextKey CreateProgramContextKey(SkslBackendBudget budget)
            => SkRuntimeEffectProgramCache.CreateContextKey(_programCacheContext, budget);

        private ProgramCacheLease<CachedSkRuntimeEffect> AcquireStandaloneProgram(
            long subjectId,
            EffectTarget target,
            string source)
        {
            ArgumentNullException.ThrowIfNull(target);
            RenderTarget renderTarget = target.RenderTarget
                ?? throw new InvalidOperationException(
                    "A legacy shader program requires a materialized execution destination.");
            return AcquireStandaloneProgram(subjectId, renderTarget, source);
        }

        private ProgramCacheLease<CachedSkRuntimeEffect> AcquireStandaloneProgram(
            long subjectId,
            RenderTarget target,
            string source)
        {
            try
            {
                ProgramCacheLease<CachedSkRuntimeEffect> lease =
                    SkRuntimeEffectProgramCache.AcquireForDestination(
                        _programCache,
                        target,
                        source);
                _diagnostics?.RecordProgramCacheDecision(subjectId, lease.IsCacheHit);
                if (lease.IsCacheHit)
                    _programCacheHits++;
                return lease;
            }
            catch
            {
                RecordFailure(RenderPipelineFailurePhase.ProgramCompilation, subjectId);
                throw;
            }
        }

        private static void SetUniform(
            SKRuntimeEffectUniforms uniforms,
            string name,
            SkslUniformDeclaration declaration,
            ShaderUniformValue value)
        {
            if (value.IsInteger)
            {
                uniforms[name] = declaration.ArrayExtent is null
                    && declaration.Type is "int" or "bool"
                        ? value.Integers![0]
                        : value.Integers!;
            }
            else
            {
                uniforms[name] = declaration.ArrayExtent is null
                    && declaration.Type is "float" or "half"
                        ? value.Floats![0]
                        : value.Floats!;
            }
        }

        private void ExecuteShaderElement(
            long subjectId,
            ShaderDescription description,
            CompatibilityRenderValue input,
            CompatibilityRenderValue output,
            Rect outputBounds,
            Rect requiredRegion)
        {
            using SKImage inputImage = input.Target.Value.Snapshot();
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
                AcquireStandaloneProgram(subjectId, output.Target, programSource);
            using var uniforms = new SKRuntimeEffectUniforms(lease.Program.Effect);
            using var runtimeChildren = new SKRuntimeEffectChildren(lease.Program.Effect);
            var children = new List<SKShader>();
            var bindingToken = new RenderExecutionSessionToken();
            try
            {
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
                                output.DeviceBounds,
                                output.RasterBounds,
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
                                SetUniform(uniforms, binding.Name, declaration, value);
                            }

                            SKShader inputShader = RasterShaderMapping.CreateSemanticImageShader(
                                inputImage,
                                input.Target.Value.Context,
                                input.Bounds,
                                input.EffectiveScale.Value,
                                input.DeviceBounds,
                                input.RasterBounds,
                                output.EffectiveScale.Value,
                                output.RasterBounds,
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
                }
                catch
                {
                    RecordFailure(RenderPipelineFailurePhase.Binding, subjectId);
                    throw;
                }

                using SKShader shader = lease.Program.Effect.ToShader(uniforms, runtimeChildren);
                using var paint = new SKPaint { Shader = shader };
                using var canvas = ImmediateCanvas.CreateExecutorManaged(
                    output.Target,
                    output.EffectiveScale.Value,
                    _options.MaxWorkingScale,
                    output.RasterBounds.Size,
                    _options.Intent,
                    output.DeviceBounds.Position);
                canvas.Clear();
                using (canvas.PushDeviceSpace())
                {
                    canvas.Canvas.DrawRect(
                        SKRect.Create(output.Target.Width, output.Target.Height),
                        paint);
                }
                _diagnostics?.RecordGpuPassExecuted(subjectId);
            }
            finally
            {
                foreach (SKShader child in children.AsEnumerable().Reverse())
                    child.Dispose();
            }
        }

    }
}
