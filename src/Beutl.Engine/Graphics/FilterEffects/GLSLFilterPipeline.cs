using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;
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
    private bool _disposed;

    private GLSLFilterPipeline(
        IGraphicsContext context,
        IRenderPass3D renderPass,
        IPipeline3D pipeline,
        ISampler sampler,
        byte[] vertexShaderSpirv,
        byte[] fragmentShaderSpirv)
    {
        _context = context;
        _renderPass = renderPass;
        _pipeline = pipeline;
        _sampler = sampler;
        _vertexShaderSpirv = vertexShaderSpirv;
        _fragmentShaderSpirv = fragmentShaderSpirv;
    }

    public static GLSLFilterPipeline? Create(IGraphicsContext context, string fragmentShaderSource)
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

            // Create render pass for BGRA8 format (matching RenderTarget format)
            IRenderPass3D renderPass = context.CreateRenderPass3D(
                [TextureFormat.BGRA8Unorm],
                TextureFormat.Depth32Float,
                AttachmentLoadOp.DontCare,
                AttachmentLoadOp.DontCare);

            // Create sampler
            ISampler sampler = context.CreateSampler(
                SamplerFilter.Linear,
                SamplerFilter.Linear,
                SamplerAddressMode.ClampToEdge,
                SamplerAddressMode.ClampToEdge);

            // Define descriptor bindings for the source texture
            var descriptorBindings = new DescriptorBinding[]
            {
                new(0, DescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment)
            };

            // Create pipeline with fullscreen options
            IPipeline3D pipeline = context.CreatePipeline3D(
                renderPass,
                vertexShaderSpirv,
                fragmentShaderSpirv,
                descriptorBindings,
                VertexInputDescription.Empty,
                PipelineOptions.Fullscreen);

            return new GLSLFilterPipeline(
                context,
                renderPass,
                pipeline,
                sampler,
                vertexShaderSpirv,
                fragmentShaderSpirv);
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
        ITexture2D depthTexture,
        T pushConstants) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Prepare textures for their respective operations
        sourceTexture.PrepareForSampling();
        destinationTexture.PrepareForRender();

        // Create framebuffer
        using IFramebuffer3D framebuffer = _context.CreateFramebuffer3D(
            _renderPass,
            [destinationTexture],
            depthTexture);

        // Create descriptor set and bind source texture
        using IDescriptorSet descriptorSet = _context.CreateDescriptorSet(
            _pipeline,
            [new DescriptorPoolSize(DescriptorType.CombinedImageSampler, 1)]);
        descriptorSet.UpdateTexture(0, sourceTexture, _sampler);

        // Execute render pass
        _renderPass.Begin(framebuffer, [default], 1.0f);
        _renderPass.BindPipeline(_pipeline);
        _renderPass.BindDescriptorSet(_pipeline, descriptorSet);
        _renderPass.SetPushConstants(pushConstants, ShaderStage.Fragment);
        _renderPass.Draw(3); // Fullscreen triangle
        _renderPass.End();

        // Prepare destination for sampling (next stage)
        destinationTexture.PrepareForSampling();
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
