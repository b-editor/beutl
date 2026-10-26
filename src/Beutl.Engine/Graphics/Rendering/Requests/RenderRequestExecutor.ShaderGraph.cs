using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Shaders;
using Beutl.Media;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RenderRequestExecutor
{
    private sealed partial class RenderRequestExecutionState
    {
        private ProgramCacheLease<ShaderGraphPipeline> AcquireShaderGraphProgram(VulkanContext context, ShaderPassDescription pass)
            => ShaderGraphPipeline.Acquire(_graphProgramCache,
                SpirvShaderProgramCache.CreateContextKey(_programCacheContext), context, pass);

        private IReadOnlyList<MaterializedRenderValue> ExecuteShaderGraph(RenderFragmentReference fragment,
            ImmediateCanvas currentTarget, EffectiveScale? requestedScale)
            => ExecuteOnDeviceGrid(currentTarget, () => ExecuteShaderGraphCore(fragment, currentTarget, requestedScale));

        private IReadOnlyList<MaterializedRenderValue> ExecuteShaderGraphCore(RenderFragmentReference fragment,
            ImmediateCanvas currentTarget, EffectiveScale? requestedScale)
        {
            var graph = ((ShaderGraphRenderFragmentPayload)fragment.Payload!).Description;
            EffectiveScale scale = requestedScale ?? fragment.EffectiveScale;
            IReadOnlyList<MaterializedRenderValue> inputs = Materialize(fragment.Inputs[0], currentTarget,
                fragment.Inputs[0].EffectiveScale.IsUnbounded ? scale : null);
            var results = new List<MaterializedRenderValue>();
            try
            {
                foreach (MaterializedRenderValue input in inputs)
                {
                    Rect bounds = graph.TransformBounds(input.CompleteBounds);
                    Rect required = ResolveFragmentRequirement(fragment, bounds);
                    if (bounds.IsEmpty || required.IsEmpty)
                        continue;
                    float density = graph.ClampWorkingScale(input.CompleteBounds, scale.Value, _activeDeviceGridOffset);
                    MaterializedRenderValue output = CreateOwnedValue(bounds, EffectiveScale.At(density), bounds,
                        allowPreviewDrop: true);
                    bool keep = false;
                    MaterializedRenderValue sourceValue = input;
                    try
                    {
                        // A source may retain a raster apron for direct replay. A graph's normalized UVs
                        // cover its complete input footprint, not that implementation-specific apron.
                        PixelRect footprint = PixelRect.FromRect(input.CompleteBounds.Translate(input.DeviceGridOffset),
                            input.EffectiveScale.Value);
                        if (input.DeviceBounds != footprint || input.Bounds != input.CompleteBounds)
                            sourceValue = CropValue(input, input.CompleteBounds, allowPreviewDrop: true);
                        ITexture2D source = sourceValue.Target.Texture
                            ?? throw new NotSupportedException("Shader graphs require backend textures; CPU rendering is not a compatible fallback.");
                        ITexture2D destination = output.Target.Texture
                            ?? throw new NotSupportedException("Shader graph output requires a backend texture.");
                        sourceValue.Target.PrepareForSampling(RenderTargetSamplingIntent.BackendInterop);
                        _programCacheHits += ShaderGraphExecutor.Execute(graph, source,
                            new(sourceValue.CompleteBounds, sourceValue.RasterBounds, sourceValue.DeviceBounds.Size, sourceValue.EffectiveScale.Value),
                            destination, density, _activeDeviceGridOffset, AcquireShaderGraphProgram);
                        _shaderRunExecutions++;
                        _spirvShaderRunExecutions++;
                        _shaderStageExecutions += graph.Passes.Length;
                        if (required != bounds)
                        {
                            MaterializedRenderValue cropped = CropValue(output, required, allowPreviewDrop: true);
                            ReleaseUnpublished(output);
                            output = cropped;
                        }
                        results.Add(output);
                        keep = true;
                    }
                    finally
                    {
                        if (!ReferenceEquals(sourceValue, input))
                            ReleaseUnpublished(sourceValue);
                        if (!keep)
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
    }
}
