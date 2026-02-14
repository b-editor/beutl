using Beutl.Graphics.Backend;
using Beutl.Media;

namespace Beutl.Graphics3D.Nodes;

/// <summary>
/// Flip pass for correcting vertical orientation.
/// Renders the input texture with flipped Y coordinates to resolve coordinate system differences.
/// </summary>
public sealed class FlipPass : GraphicsNode3D
{
    private IPipeline3D? _pipeline;
    private IDescriptorSet? _descriptorSet;
    private ISampler? _sampler;
    private ITexture2D? _inputTexture;
    private ITexture2D? _depthTexture;

    public FlipPass(IGraphicsContext context, IShaderCompiler shaderCompiler)
        : base(context, shaderCompiler)
    {
    }

    /// <summary>
    /// Gets the output texture with corrected orientation.
    /// </summary>
    public ITexture2D? OutputTexture { get; private set; }

    protected override void OnInitialize(int width, int height)
    {
        CreateResources(width, height);
    }

    protected override void OnResize(int width, int height)
    {
        CreateResources(width, height);
    }

    private void CreateResources(int width, int height)
    {
        // Dispose old resources
        Framebuffer?.Dispose();
        RenderPass?.Dispose();
        OutputTexture?.Dispose();
        _depthTexture?.Dispose();
        _pipeline?.Dispose();
        _descriptorSet?.Dispose();
        _sampler?.Dispose();

        // Create output and depth textures
        OutputTexture = Context.CreateTexture2D(width, height, TextureFormat.RGBA8Unorm);
        _depthTexture = Context.CreateTexture2D(width, height, TextureFormat.Depth32Float);

        // Create render pass and framebuffer
        RenderPass = Context.CreateRenderPass3D([TextureFormat.RGBA8Unorm], TextureFormat.Depth32Float);
        Framebuffer = Context.CreateFramebuffer3D(RenderPass, [OutputTexture], _depthTexture);

        // Create pipeline
        CreatePipeline();
    }

    private void CreatePipeline()
    {
        // Create sampler
        _sampler = Context.CreateSampler(
            SamplerFilter.Linear,
            SamplerFilter.Linear,
            SamplerAddressMode.ClampToEdge,
            SamplerAddressMode.ClampToEdge);

        // Compile shaders
        var vertexSpirv = ShaderCompiler.CompileToSpirv(FlipVertexShader, ShaderStage.Vertex);
        var fragmentSpirv = ShaderCompiler.CompileToSpirv(FlipFragmentShader, ShaderStage.Fragment);

        // Descriptor bindings: 1 input texture sampler
        var descriptorBindings = new DescriptorBinding[]
        {
            new(0, DescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment)
        };

        // Use fullscreen pipeline
        _pipeline = Context.CreatePipeline3D(
            RenderPass!,
            vertexSpirv,
            fragmentSpirv,
            descriptorBindings,
            VertexInputDescription.Empty,
            PipelineOptions.Fullscreen);

        // Create descriptor set
        var poolSizes = new DescriptorPoolSize[]
        {
            new(DescriptorType.CombinedImageSampler, 1)
        };

        _descriptorSet = Context.CreateDescriptorSet(_pipeline, poolSizes);
    }

    /// <summary>
    /// Sets the input texture to be flipped.
    /// </summary>
    public void SetInputTexture(ITexture2D inputTexture)
    {
        _inputTexture = inputTexture;

        if (_descriptorSet != null && _sampler != null)
        {
            _descriptorSet.UpdateTexture(0, inputTexture, _sampler);
        }
    }

    /// <summary>
    /// Executes the flip pass.
    /// </summary>
    public void Execute()
    {
        if (Framebuffer == null || RenderPass == null || _pipeline == null || _descriptorSet == null || _inputTexture == null)
            return;

        // Begin flip pass
        Span<Color> clearColors = [new Color(0, 0, 0, 255)];
        BeginPass(clearColors);

        // Bind pipeline and descriptor set
        RenderPass.BindPipeline(_pipeline);
        RenderPass.BindDescriptorSet(_pipeline, _descriptorSet);

        // Draw fullscreen triangle
        RenderPass.Draw(3);

        EndPass();
    }

    protected override void OnDispose()
    {
        _descriptorSet?.Dispose();
        _pipeline?.Dispose();
        _sampler?.Dispose();
        Framebuffer?.Dispose();
        RenderPass?.Dispose();
        OutputTexture?.Dispose();
        _depthTexture?.Dispose();
    }

    // === Flip Pass Shaders ===

    private static string FlipVertexShader => """
        #version 450

        layout(location = 0) out vec2 fragTexCoord;

        void main() {
            // Generate fullscreen triangle vertices
            vec2 positions[3] = vec2[](
                vec2(-1.0, -1.0),
                vec2(3.0, -1.0),
                vec2(-1.0, 3.0)
            );

            // Flipped texture coordinates (Y is inverted)
            vec2 texCoords[3] = vec2[](
                vec2(0.0, 1.0),  // Bottom-left -> Top-left of texture
                vec2(2.0, 1.0),  // Bottom-right -> Top-right of texture
                vec2(0.0, -1.0)  // Top-left -> Bottom-left of texture
            );

            gl_Position = vec4(positions[gl_VertexIndex], 0.0, 1.0);
            fragTexCoord = texCoords[gl_VertexIndex];
        }
        """;

    private static string FlipFragmentShader => """
        #version 450

        layout(location = 0) in vec2 fragTexCoord;

        layout(binding = 0) uniform sampler2D inputTexture;

        layout(location = 0) out vec4 outColor;

        void main() {
            outColor = texture(inputTexture, fragTexCoord);
        }
        """;
}
