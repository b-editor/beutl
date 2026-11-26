using Beutl.Graphics.Backend;
using Beutl.Media;

namespace Beutl.Graphics3D.Nodes;

/// <summary>
/// Flip pass for correcting vertical orientation.
/// Renders the input texture with flipped Y coordinates to resolve coordinate system differences.
/// </summary>
public sealed class FlipPass : GraphicsNode3D
{
    private (IPipeline3D Pipeline, IDescriptorSet Descriptors)? _singleSamplePipeline;
    private (IPipeline3D Pipeline, IDescriptorSet Descriptors)? _multisamplePipeline;
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
        // This pass allocates the extent it is handed, so the device limit is asked here, before anything
        // is disposed: a refused resize keeps the resources it had.
        DeviceExtentLimits.ThrowIfCannotAttach(Context, width, height);
        BeginReplacingResources();

        Framebuffer?.Dispose();
        RenderPass?.Dispose();
        OutputTexture?.Dispose();
        _depthTexture?.Dispose();
        DisposePipelines();

        OutputTexture = Context.CreateTexture2D(width, height, TextureFormat.RGBA16Float);
        _depthTexture = Context.CreateTexture2D(width, height, TextureFormat.Depth32Float);

        RenderPass = Context.CreateRenderPass3D([TextureFormat.RGBA16Float], TextureFormat.Depth32Float);
        Framebuffer = Context.CreateFramebuffer3D(RenderPass, [OutputTexture], _depthTexture);

        _sampler = Context.CreateSampler(
            SamplerFilter.Linear,
            SamplerFilter.Linear,
            SamplerAddressMode.ClampToEdge,
            SamplerAddressMode.ClampToEdge);
        var pipeline = GetOrCreatePipeline(_inputTexture is { SampleCount: > 1 });
        if (_inputTexture is { } input)
            pipeline.Descriptors.UpdateTexture(0, input, _sampler);
    }

    private (IPipeline3D Pipeline, IDescriptorSet Descriptors) GetOrCreatePipeline(bool multisampled)
        => multisampled
            ? _multisamplePipeline ??= CreatePipeline(MultisampleFlipFragmentShader)
            : _singleSamplePipeline ??= CreatePipeline(FlipFragmentShader);

    private (IPipeline3D Pipeline, IDescriptorSet Descriptors) CreatePipeline(string fragmentShader)
    {
        var vertexSpirv = ShaderCompiler.CompileToSpirv(FlipVertexShader, ShaderStage.Vertex);
        var fragmentSpirv = ShaderCompiler.CompileToSpirv(fragmentShader, ShaderStage.Fragment);

        var descriptorBindings = new DescriptorBinding[]
        {
            new(0, DescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment)
        };

        IPipeline3D pipeline = Context.CreatePipeline3D(
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

        try
        {
            return (pipeline, Context.CreateDescriptorSet(pipeline, poolSizes));
        }
        catch
        {
            pipeline.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Sets the input texture to be flipped.
    /// </summary>
    public void SetInputTexture(ITexture2D inputTexture)
    {
        _inputTexture = inputTexture;

        if (_sampler != null)
        {
            GetOrCreatePipeline(inputTexture.SampleCount > 1).Descriptors.UpdateTexture(0, inputTexture, _sampler);
        }
    }

    /// <summary>
    /// Executes the flip pass.
    /// </summary>
    public void Execute()
    {
        if (Framebuffer == null || RenderPass == null || _inputTexture == null)
            return;

        var pipeline = _inputTexture.SampleCount > 1 ? _multisamplePipeline : _singleSamplePipeline;
        if (pipeline is not { } binding)
            return;

        Span<Color> clearColors = [new Color(0, 0, 0, 255)];
        using (UsePass(clearColors))
        {
            RenderPass.BindPipeline(binding.Pipeline);
            RenderPass.BindDescriptorSet(binding.Pipeline, binding.Descriptors);

            RenderPass.Draw(3);
        }
    }

    protected override void OnDispose()
    {
        DisposeResources([_singleSamplePipeline?.Descriptors, _singleSamplePipeline?.Pipeline,
            _multisamplePipeline?.Descriptors, _multisamplePipeline?.Pipeline, _sampler,
            Framebuffer, RenderPass, OutputTexture, _depthTexture]);
    }

    private void DisposePipelines()
    {
        DisposeResources([_singleSamplePipeline?.Descriptors, _singleSamplePipeline?.Pipeline,
            _multisamplePipeline?.Descriptors, _multisamplePipeline?.Pipeline, _sampler]);
        _singleSamplePipeline = null;
        _multisamplePipeline = null;
        _sampler = null;
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

    private const string MultisampleFlipFragmentShader = """
        #version 450
        layout(location = 0) in vec2 fragTexCoord;
        layout(binding = 0) uniform sampler2DMS inputTexture;
        layout(location = 0) out vec4 outColor;

        void main() {
            ivec2 size = textureSize(inputTexture);
            ivec2 pixel = clamp(ivec2(fragTexCoord * vec2(size)), ivec2(0), size - 1);
            int count = textureSamples(inputTexture);
            vec4 sum = vec4(0.0);
            for (int i = 0; i < count; i++) {
                sum += texelFetch(inputTexture, pixel, i);
            }
            outColor = sum / float(count);
        }
        """;
}
