using Beutl.Graphics.Backend;

namespace Beutl.Graphics3D.Nodes;

/// <summary>Resolves multisampled scene depth for readback before gizmos clear the shared depth.</summary>
internal sealed class DepthResolvePass(IGraphicsContext context, IShaderCompiler shaderCompiler, ITexture2D source)
    : GraphicsNode3D(context, shaderCompiler)
{
    private IPipeline3D? _pipeline;
    private IDescriptorSet? _descriptors;
    private ISampler? _sampler;

    public ITexture2D? OutputTexture { get; private set; }

    protected override void OnInitialize(int width, int height)
    {
        DeviceExtentLimits.ThrowIfCannotAttach(Context, width, height);
        OutputTexture = Context.CreateTexture2D(width, height, TextureFormat.Depth32Float);
        RenderPass = Context.CreateRenderPass3D([], TextureFormat.Depth32Float);
        Framebuffer = Context.CreateFramebuffer3D(RenderPass, [], OutputTexture);

        PipelineOptions options = PipelineOptions.Fullscreen;
        options.DepthTestEnabled = true;
        options.DepthWriteEnabled = true;
        options.DepthCompare = CompareFunction.Always;
        _pipeline = Context.CreatePipeline3D(
            RenderPass,
            ShaderCompiler.CompileToSpirv(VertexShader, ShaderStage.Vertex),
            ShaderCompiler.CompileToSpirv(FragmentShader, ShaderStage.Fragment),
            [new DescriptorBinding(0, DescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment)],
            VertexInputDescription.Empty,
            options);
        _sampler = Context.CreateSampler(SamplerFilter.Nearest, SamplerFilter.Nearest);
        _descriptors = Context.CreateDescriptorSet(_pipeline, [new DescriptorPoolSize(DescriptorType.CombinedImageSampler, 1)]);
        _descriptors.UpdateTexture(0, source, _sampler);
    }

    protected override void OnResize(int width, int height)
    {
        DeviceExtentLimits.ThrowIfCannotAttach(Context, width, height);
        BeginReplacingResources();
        OnDispose();
        OnInitialize(width, height);
    }

    public void Execute()
    {
        source.PrepareForSampling();
        using (UsePass([]))
        {
            RenderPass!.BindPipeline(_pipeline!);
            RenderPass.BindDescriptorSet(_pipeline!, _descriptors!);
            RenderPass.Draw(3);
        }
    }

    protected override void OnDispose()
    {
        DisposeResources([_descriptors, _pipeline, _sampler, Framebuffer, RenderPass, OutputTexture]);
    }

    private const string VertexShader = """
        #version 450

        void main() {
            vec2 positions[3] = vec2[](vec2(-1.0, -1.0), vec2(3.0, -1.0), vec2(-1.0, 3.0));
            gl_Position = vec4(positions[gl_VertexIndex], 0.0, 1.0);
        }
        """;

    private const string FragmentShader = """
        #version 450
        layout(binding = 0) uniform sampler2DMS sceneDepth;

        void main() {
            ivec2 pixel = ivec2(gl_FragCoord.xy);
            float nearestDepth = 1.0;
            int count = textureSamples(sceneDepth);
            for (int i = 0; i < count; i++) {
                nearestDepth = min(nearestDepth, texelFetch(sceneDepth, pixel, i).r);
            }
            gl_FragDepth = nearestDepth;
        }
        """;
}
