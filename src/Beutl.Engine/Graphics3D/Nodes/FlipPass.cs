using Beutl.Graphics.Backend;
using Beutl.Media;

namespace Beutl.Graphics3D.Nodes;

/// <summary>
/// Flip pass for correcting vertical orientation.
/// Renders the input texture with flipped Y coordinates to resolve coordinate system differences.
/// </summary>
public sealed class FlipPass : RenderNode3D
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
        // This pass allocates the extent it is handed, so the device limit is asked here, before anything
        // is allocated.
        DeviceExtentLimits.ThrowIfCannotAttach(Context, width, height);

        OutputTexture = Context.CreateTexture2D(width, height, TextureFormat.RGBA16Float);
        _depthTexture = Context.CreateTexture2D(width, height, TextureFormat.Depth32Float);

        RenderPass = Context.CreateRenderPass3D([TextureFormat.RGBA16Float], TextureFormat.Depth32Float);
        Framebuffer = Context.CreateFramebuffer3D(RenderPass, [OutputTexture], _depthTexture);

        CreatePipeline();
    }

    private void CreatePipeline()
    {
        _sampler = Context.CreateSampler(
            SamplerFilter.Linear,
            SamplerFilter.Linear,
            SamplerAddressMode.ClampToEdge,
            SamplerAddressMode.ClampToEdge);

        var vertexSpirv = ShaderCompiler.CompileToSpirv(FlipVertexShader, ShaderStage.Vertex);
        var fragmentSpirv = ShaderCompiler.CompileToSpirv(FlipFragmentShader, ShaderStage.Fragment);

        var descriptorBindings = new DescriptorBinding[]
        {
            new(0, DescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment)
        };

        _pipeline = Context.CreatePipeline3D(
            RenderPass!,
            vertexSpirv,
            fragmentSpirv,
            descriptorBindings,
            VertexInputDescription.Empty,
            PipelineOptions.Fullscreen);

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

        Span<Color> clearColors = [new Color(0, 0, 0, 255)];
        using (UsePass(clearColors))
        {
            RenderPass.BindPipeline(_pipeline);
            RenderPass.BindDescriptorSet(_pipeline, _descriptorSet);

            RenderPass.Draw(3);
        }
    }

    protected override void OnDispose()
    {
        DisposeResources([_descriptorSet, _pipeline, _sampler, Framebuffer, RenderPass, OutputTexture, _depthTexture]);
    }

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
