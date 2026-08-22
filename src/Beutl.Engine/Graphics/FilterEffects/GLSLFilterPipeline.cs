using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Composite;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Effects;

internal sealed class GLSLFilterPipeline : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<GLSLFilterPipeline>();

    // Fullscreen triangle vertex shader that generates UV coordinates
    private const string FullscreenVertexShader = """
        #version 450

        layout(location = 0) out vec2 fragCoord;

        void main() {
            // Generate fullscreen triangle vertices
            // Vertex 0: (-1, -1), UV (0, 0)
            // Vertex 1: (3, -1), UV (2, 0)
            // Vertex 2: (-1, 3), UV (0, 2)
            vec2 positions[3] = vec2[](
                vec2(-1.0, -1.0),
                vec2(3.0, -1.0),
                vec2(-1.0, 3.0)
            );
            vec2 uvs[3] = vec2[](
                vec2(0.0, 0.0),
                vec2(2.0, 0.0),
                vec2(0.0, 2.0)
            );
            gl_Position = vec4(positions[gl_VertexIndex], 0.0, 1.0);
            fragCoord = uvs[gl_VertexIndex];
        }
        """;

    private readonly IGraphicsContext _context;
    private readonly IRenderPass3D _renderPass;
    private readonly IPipeline3D _pipeline;
    private readonly ISampler _sampler;
    private readonly byte[] _vertexShaderSpirv;
    private readonly byte[] _fragmentShaderSpirv;
    private readonly ShaderOutputCoverage _outputCoverage;
    private bool _disposed;

    private readonly bool _hasMaskTexture;

    internal bool HasMaskTexture => _hasMaskTexture;

    internal long RetainedByteSize => Math.Max(1, _vertexShaderSpirv.Length + _fragmentShaderSpirv.Length);

    private GLSLFilterPipeline(
        IGraphicsContext context,
        IRenderPass3D renderPass,
        IPipeline3D pipeline,
        ISampler sampler,
        byte[] vertexShaderSpirv,
        byte[] fragmentShaderSpirv,
        ShaderOutputCoverage outputCoverage,
        bool hasMaskTexture = false)
    {
        _context = context;
        _renderPass = renderPass;
        _pipeline = pipeline;
        _sampler = sampler;
        _vertexShaderSpirv = vertexShaderSpirv;
        _fragmentShaderSpirv = fragmentShaderSpirv;
        _outputCoverage = outputCoverage;
        _hasMaskTexture = hasMaskTexture;
    }

    /// <summary>
    /// Compiles a fragment shader and creates its fullscreen filter pipeline.
    /// </summary>
    /// <param name="context">The graphics context that owns the pipeline.</param>
    /// <param name="fragmentShaderSource">The GLSL fragment shader source.</param>
    /// <param name="outputCoverage">
    /// The proven fragment-output coverage contract. <see cref="ShaderOutputCoverage.MayLeavePixelsUnwritten"/>
    /// transparently initializes the destination before the pass and clears the render-pass attachment.
    /// <see cref="ShaderOutputCoverage.ProvablyFull"/> may be selected only for an engine-owned shader whose
    /// every control-flow path writes the fragment output and which never uses <c>discard</c>; a false claim can
    /// expose stale pixels from an unrelated frame when a pooled target is reused.
    /// </param>
    /// <param name="specializationConstants">Immutable values applied when the pipeline is created.</param>
    /// <param name="hasMaskTexture">Whether the shader reads a second texture at binding 1.</param>
    public static GLSLFilterPipeline? Create(
        IGraphicsContext context,
        string fragmentShaderSource,
        ShaderOutputCoverage outputCoverage,
        ImmutableArray<SpecializationConstant> specializationConstants = default,
        bool hasMaskTexture = false)
    {
        if (!context.Supports3DRendering)
        {
            s_logger.LogWarning("3D rendering is not supported on this platform.");
            return null;
        }

        try
        {
            IShaderCompiler compiler = context.CreateShaderCompiler();

            // Compile vertex shader
            byte[] vertexShaderSpirv = compiler.CompileToSpirv(FullscreenVertexShader, ShaderStage.Vertex);

            // Compile fragment shader
            byte[] fragmentShaderSpirv = compiler.CompileToSpirv(fragmentShaderSource, ShaderStage.Fragment);

            // Create a color-only render pass matching the RenderTarget format.
            IRenderPass3D renderPass = context.CreateRenderPass3D(
                [TextureFormat.RGBA16Float],
                depthFormat: null,
                colorLoadOp: outputCoverage == ShaderOutputCoverage.ProvablyFull
                    ? AttachmentLoadOp.DontCare
                    : AttachmentLoadOp.Clear);

            // Create sampler
            ISampler sampler = context.CreateSampler(
                SamplerFilter.Linear,
                SamplerFilter.Linear,
                SamplerAddressMode.ClampToEdge,
                SamplerAddressMode.ClampToEdge);

            // Define descriptor bindings (1 or 2 textures)
            DescriptorBinding[] descriptorBindings = hasMaskTexture
                ? [
                    new(0, DescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment),
                    new(1, DescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment)
                  ]
                : [new(0, DescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment)];

            // Create pipeline with fullscreen options
            PipelineOptions pipelineOptions = PipelineOptions.Fullscreen;
            pipelineOptions.SpecializationConstants = specializationConstants;
            IPipeline3D pipeline = context.CreatePipeline3D(
                renderPass,
                vertexShaderSpirv,
                fragmentShaderSpirv,
                descriptorBindings,
                VertexInputDescription.Empty,
                pipelineOptions);

            return new GLSLFilterPipeline(
                context,
                renderPass,
                pipeline,
                sampler,
                vertexShaderSpirv,
                fragmentShaderSpirv,
                outputCoverage,
                hasMaskTexture);
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Failed to create GLSL filter pipeline.");
            return null;
        }
    }

    public void Execute<T>(
        ITexture2D sourceTexture,
        ITexture2D destinationTexture,
        T pushConstants) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_hasMaskTexture)
            throw new InvalidOperationException("This pipeline requires a mask texture. Use the dual-texture Execute overload.");

        // Prepare textures for their respective operations
        sourceTexture.PrepareForSampling();
        PrepareDestination(destinationTexture);

        // Create framebuffer
        using IFramebuffer3D framebuffer = _context.CreateFramebuffer3D(
            _renderPass,
            [destinationTexture],
            depthTexture: null);

        // Create descriptor set and bind source texture
        using IDescriptorSet descriptorSet = _context.CreateDescriptorSet(
            _pipeline,
            [new DescriptorPoolSize(DescriptorType.CombinedImageSampler, 1)]);
        descriptorSet.UpdateTexture(0, sourceTexture, _sampler);

        // Execute render pass. The pass holds a render-pass scope on the context-wide recording batch,
        // so a body that throws has to release it or every later transfer in the process is diverted.
        _renderPass.Begin(framebuffer, [default]);
        try
        {
            _renderPass.BindPipeline(_pipeline);
            _renderPass.BindDescriptorSet(_pipeline, descriptorSet);
            _renderPass.SetPushConstants(pushConstants, ShaderStage.Fragment);
            _renderPass.Draw(3); // Fullscreen triangle
        }
        finally
        {
            _renderPass.End();
        }

        // Prepare destination for sampling (next stage)
        destinationTexture.PrepareForSampling();
    }

    // Overload for dual-texture pipelines (source + mask)
    public void Execute<T>(
        ITexture2D sourceTexture,
        ITexture2D maskTexture,
        ITexture2D destinationTexture,
        T pushConstants) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_hasMaskTexture)
            throw new InvalidOperationException("This pipeline was not created with mask texture support.");

        // Prepare textures for their respective operations
        sourceTexture.PrepareForSampling();
        maskTexture.PrepareForSampling();
        PrepareDestination(destinationTexture);

        // Create framebuffer
        using IFramebuffer3D framebuffer = _context.CreateFramebuffer3D(
            _renderPass,
            [destinationTexture],
            depthTexture: null);

        // Create descriptor set and bind both textures
        using IDescriptorSet descriptorSet = _context.CreateDescriptorSet(
            _pipeline,
            [new DescriptorPoolSize(DescriptorType.CombinedImageSampler, 2)]);
        descriptorSet.UpdateTexture(0, sourceTexture, _sampler);
        descriptorSet.UpdateTexture(1, maskTexture, _sampler);

        // Execute render pass. The pass holds a render-pass scope on the context-wide recording batch,
        // so a body that throws has to release it or every later transfer in the process is diverted.
        _renderPass.Begin(framebuffer, [default]);
        try
        {
            _renderPass.BindPipeline(_pipeline);
            _renderPass.BindDescriptorSet(_pipeline, descriptorSet);
            _renderPass.SetPushConstants(pushConstants, ShaderStage.Fragment);
            _renderPass.Draw(3); // Fullscreen triangle
        }
        finally
        {
            _renderPass.End();
        }

        // Prepare destination for sampling (next stage)
        destinationTexture.PrepareForSampling();
    }

    private void PrepareDestination(ITexture2D destinationTexture)
    {
        if (_outputCoverage == ShaderOutputCoverage.MayLeavePixelsUnwritten)
        {
            if (destinationTexture is not ITransparentClearableTexture clearableTexture)
            {
                throw new InvalidOperationException(
                    "A conservative native shader requires an ordered transparent-clear texture.");
            }

            clearableTexture.ClearToTransparent();
        }

        destinationTexture.PrepareForRender();
    }

    internal void SubmitPendingCommands()
    {
        // A subsequent effect may consume this output through another backend immediately. Submit the recorded
        // clears and draws so their queue order is established before the caller releases the source target.
        VulkanContext context = _context switch
        {
            VulkanContext vulkan => vulkan,
            CompositeContext composite => composite.Vulkan,
            _ => throw new InvalidOperationException("The GLSL pipeline requires a Vulkan recording context."),
        };
        context.FlushCommands(waitForCompletion: false);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _pipeline.Dispose();
        _renderPass.Dispose();
        _sampler.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// Declares whether a fragment shader is proven to write every output pixel, allowing a filter render pass to
/// load a transparently initialized destination or discard its previous contents safely.
/// </summary>
/// <remarks>
/// Only engine-owned built-in shaders may claim <see cref="ProvablyFull"/>, and only after proving that every
/// control-flow path writes the fragment output and that the shader contains no <c>discard</c>. A false claim
/// leaves unwritten pixels unchanged, so a reused pooled target can reveal stale pixels from a previous,
/// unrelated frame; this failure may not reproduce while the pool is cold.
/// </remarks>
internal enum ShaderOutputCoverage : byte
{
    /// <summary>
    /// The shader may leave fragments unwritten, so the destination is transparently initialized and the
    /// render-pass attachment is cleared before drawing. Public or user-authored shaders always use this
    /// conservative contract.
    /// </summary>
    MayLeavePixelsUnwritten,

    /// <summary>
    /// Every control-flow path is proven to write the fragment output and no path uses <c>discard</c>. Only an
    /// audited engine-owned built-in shader may claim this; an incorrect claim exposes stale pooled-target pixels
    /// from an unrelated frame and can remain hidden until a warm-pool reuse.
    /// </summary>
    ProvablyFull,
}
