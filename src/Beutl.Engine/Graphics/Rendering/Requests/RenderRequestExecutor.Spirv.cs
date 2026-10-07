using Beutl.Graphics.Backend;
using Beutl.Graphics.Shaders;
using Beutl.Media;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RenderRequestExecutor
{
    private sealed partial class RenderRequestExecutionState
    {
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
                              // The SPIR-V path reads stored values without Skia's color conversion.
                              && input.Target.ColorSpace == BitmapColorSpace.LinearSrgb
                              && output.Target.ColorSpace == BitmapColorSpace.LinearSrgb
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
                // change existing output. A short failure cooldown permits a later SPIR-V retry.
                return false;
            }
            using (lease)
            {
                PixelPoint sourceTexelOffset = output.DeviceBounds.Position - input.DeviceBounds.Position;
                SpirvPushConstants pushConstants = stageDescription!.HasExecutionContextBinder
                    ? BindSpirvPushConstantsWithCallbacks(
                        run,
                        input,
                        output,
                        outputBounds,
                        requiredRegion,
                        lowering,
                        stageDescription,
                        sourceTexelOffset)
                    : lowering.Bind(stageDescription, context: null, sourceTexelOffset);

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

        private SpirvPushConstants BindSpirvPushConstantsWithCallbacks(
            CompiledShaderRun run,
            MaterializedRenderValue input,
            MaterializedRenderValue output,
            Rect outputBounds,
            Rect requiredRegion,
            SpirvShaderLowering lowering,
            ShaderDescription description,
            PixelPoint sourceTexelOffset)
        {
            RenderExecutionSessionToken bindingToken = CreateExecutionSessionToken();
            return bindingToken.RunAndComplete(
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
                    return lowering.Bind(description, context, sourceTexelOffset);
                });
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
    }
}
