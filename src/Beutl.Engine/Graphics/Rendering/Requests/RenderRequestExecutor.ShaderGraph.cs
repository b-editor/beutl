using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Shaders;
using Beutl.Media;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RenderRequestExecutor
{
    private sealed partial class RenderRequestExecutionState
    {
        private ProgramCacheLease<ShaderGraphPipeline> AcquireShaderGraphProgram(VulkanContext context, ShaderPassDescription pass,
            ShaderProgramIdentity identity)
            => ShaderGraphPipeline.Acquire(_programCaches.ShaderGraph,
                SpirvShaderProgramCache.CreateContextKey(_programCacheContext), context, pass, identity);

        private IReadOnlyList<MaterializedRenderValue> ExecuteShaderGraph(RenderFragmentReference fragment,
            ImmediateCanvas currentTarget, EffectiveScale? requestedScale)
            => ExecuteOnDeviceGrid(currentTarget, () => ExecuteShaderGraphCore(fragment, currentTarget, requestedScale));

        private IReadOnlyList<MaterializedRenderValue> ExecuteShaderGraphCore(RenderFragmentReference fragment,
            ImmediateCanvas currentTarget, EffectiveScale? requestedScale)
        {
            var payload = (ShaderGraphRenderFragmentPayload)fragment.Payload!;
            ShaderGraphDescription graph = payload.Description;
            EffectiveScale scale = EffectiveScale.At(payload.ClampDensity(fragment.Inputs[0],
                (requestedScale ?? fragment.EffectiveScale).Value, ResolveFragmentRequirement(fragment, fragment.Bounds)));
            IReadOnlyList<MaterializedRenderValue> inputs = Materialize(fragment.Inputs[0], currentTarget,
                fragment.Inputs[0].EffectiveScale.IsUnbounded ? scale : null);
            Rect referenceBounds = inputs.Aggregate(Rect.Empty, (bounds, input) => bounds.Union(input.CompleteBounds));
            var results = new List<MaterializedRenderValue>();
            var externals = new IReadOnlyList<MaterializedRenderValue>[fragment.Inputs.Length - 1];
            var externalValues = new List<MaterializedRenderValue>();
            try
            {
                for (int index = 0; index < externals.Length; index++)
                {
                    RenderFragmentReference external = fragment.Inputs[index + 1];
                    externals[index] = Materialize(external, currentTarget, external.EffectiveScale.IsUnbounded ? scale : null);
                }

                foreach (MaterializedRenderValue input in inputs)
                {
                    Rect bounds = graph.TransformBounds(input.CompleteBounds, referenceBounds);
                    Rect required = ResolveFragmentRequirement(fragment, bounds);
                    if (bounds.Width == 0 || bounds.Height == 0 || required.Width == 0 || required.Height == 0)
                        continue;
                    MaterializedRenderValue? output = null;
                    bool keep = false;
                    MaterializedRenderValue sourceValue = input;
                    try
                    {
                        // A source may retain a raster apron for direct replay. A graph's normalized UVs
                        // cover its complete input footprint, not that implementation-specific apron,
                        // and only passes that find the source through its metadata can read a part of it.
                        Rect sourceBounds = graph.AcceptsPartialSource ? input.Bounds : input.CompleteBounds;
                        PixelRect footprint = PixelRect.FromRect(sourceBounds.Translate(input.DeviceGridOffset),
                            input.EffectiveScale.Value);
                        if (input.DeviceBounds != footprint || input.Bounds != sourceBounds)
                            sourceValue = CropValue(input, sourceBounds, allowPreviewDrop: true);
                        ITexture2D source = sourceValue.Target.Texture
                            ?? throw new NotSupportedException("Shader graphs require backend textures; CPU rendering is not a compatible fallback.");
                        float density = graph.ClampWorkingScale(input.CompleteBounds, scale.Value, _activeDeviceGridOffset,
                            referenceBounds, sourceValue.DeviceBounds.Size, ((VulkanTexture2D)source).Usage, required);
                        output = CreateOwnedValue(required, EffectiveScale.At(density), bounds, allowPreviewDrop: true);
                        ITexture2D destination = output.Target.Texture
                            ?? throw new NotSupportedException("Shader graph output requires a backend texture.");
                        sourceValue.Target.PrepareForSampling(RenderTargetSamplingIntent.BackendInterop);
                        var externalImages = new (ITexture2D Texture, ShaderImageFrame Frame)[externals.Length];
                        for (int index = 0; index < externals.Length; index++)
                        {
                            MaterializedRenderValue aligned = AlignExternalImage(externals[index], sourceValue);
                            externalValues.Add(aligned);
                            aligned.Target.PrepareForSampling(RenderTargetSamplingIntent.BackendInterop);
                            externalImages[index] = (aligned.Target.Texture
                                ?? throw new NotSupportedException("Shader graphs require backend textures; CPU rendering is not a compatible fallback."),
                                new(aligned.CompleteBounds, aligned.RasterBounds, aligned.DeviceBounds.Size, aligned.EffectiveScale.Value, referenceBounds));
                        }

                        ShaderGraphExecutionStatistics execution;
                        try
                        {
                            execution = ShaderGraphExecutor.Execute(graph, source,
                                new(sourceValue.CompleteBounds, sourceValue.RasterBounds, sourceValue.DeviceBounds.Size, sourceValue.EffectiveScale.Value, referenceBounds),
                                destination, density, _activeDeviceGridOffset, AcquireShaderGraphProgram, required,
                                _programCaches.ShaderGraphImages, externalImages);
                            graph.ExecutionObserver?.Invoke(null);
                        }
                        catch (Exception ex) when (graph.ExecutionObserver is { } observer)
                        {
                            observer(ex);
                            continue;
                        }

                        _programCacheHits += execution.ProgramCacheHits;
                        _shaderRunExecutions++;
                        _spirvShaderRunExecutions++;
                        _shaderStageExecutions += execution.ExecutedPasses;
                        results.Add(output);
                        keep = true;
                    }
                    finally
                    {
                        foreach (MaterializedRenderValue aligned in externalValues)
                            ReleaseUnpublished(aligned);
                        externalValues.Clear();
                        if (!ReferenceEquals(sourceValue, input))
                            ReleaseUnpublished(sourceValue);
                        if (!keep && output is not null)
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
                foreach (RenderFragmentReference input in fragment.Inputs)
                    CompleteFragmentUse(input);
            }
        }

        // A graph reads every picture through the same coordinates, so each one is laid out the way the source is.
        private MaterializedRenderValue AlignExternalImage(IReadOnlyList<MaterializedRenderValue> values, MaterializedRenderValue source)
        {
            MaterializedRenderValue aligned = CreateOwnedValue(source.CompleteBounds, source.EffectiveScale, source.CompleteBounds,
                source.DeviceBounds, source.DeviceGridOffset, physicalDeviceBoundsAreAligned: true, allowPreviewDrop: true);
            bool succeeded = false;
            try
            {
                Vector rasterTranslation = DeviceGridAlignment.ResolveRasterTranslation(
                    aligned.DeviceBounds, aligned.DeviceGridOffset, aligned.EffectiveScale.Value);
                using var canvas = CreateExecutorCanvas(aligned.Target, aligned.EffectiveScale.Value, _options.MaxWorkingScale,
                    aligned.RasterBounds.Size, _options.Intent, aligned.DeviceBounds.Position);
                using (canvas.PushTransform(Matrix.CreateTranslation(rasterTranslation.X, rasterTranslation.Y)))
                {
                    canvas.ClipRect(source.CompleteBounds);
                    foreach (MaterializedRenderValue value in values)
                        canvas.DrawRenderTargetScaledWithoutFlush(value.Target, value.RasterBounds);
                }

                succeeded = true;
                return aligned;
            }
            finally
            {
                if (!succeeded)
                    ReleaseUnpublished(aligned);
            }
        }
    }
}
