using System.Runtime.InteropServices;
using SDL;

namespace Beutl.Graphics3D;

public unsafe class GraphicsPipeline : GraphicsResource
{
    private readonly GraphicsPipelineCreateInfo _createInfo;

    private GraphicsPipeline(
        Device device, SDL_GPUGraphicsPipeline* handle,
        GraphicsPipelineCreateInfo createInfo) : base(device)
    {
        Handle = handle;
        _createInfo = createInfo;
    }

    public Shader VertexShader => _createInfo.VertexShader;

    public Shader FragmentShader => _createInfo.FragmentShader;

    public VertexInputState VertexInputState => _createInfo.VertexInputState;

    public PrimitiveType PrimitiveType => _createInfo.PrimitiveType;

    public RasterizerState RasterizerState => _createInfo.RasterizerState;

    public MultisampleState MultisampleState => _createInfo.MultisampleState;

    public DepthStencilState DepthStencilState => _createInfo.DepthStencilState;

    public GraphicsPipelineTargetInfo TargetInfo => _createInfo.TargetInfo;

    public uint Props => _createInfo.Props;

    internal SDL_GPUGraphicsPipeline* Handle { get; private set; }

    public static unsafe GraphicsPipeline Create(Device device, in GraphicsPipelineCreateInfo graphicsPipelineCreateInfo)
    {
        SDL_GPUGraphicsPipelineCreateInfo createInfo = default;

        var vertexAttributes = (SDL_GPUVertexAttribute*)NativeMemory.Alloc(
            (nuint)(graphicsPipelineCreateInfo.VertexInputState.VertexAttributes.Length * sizeof(SDL_GPUVertexAttribute)));

        for (int i = 0; i < graphicsPipelineCreateInfo.VertexInputState.VertexAttributes.Length; i++)
        {
            vertexAttributes[i] = graphicsPipelineCreateInfo.VertexInputState.VertexAttributes[i].ToNative();
        }

        var vertexBindings = (SDL_GPUVertexBufferDescription*)NativeMemory.Alloc(
            (nuint)(graphicsPipelineCreateInfo.VertexInputState.VertexBufferDescriptions.Length * sizeof(SDL_GPUVertexBufferDescription)));

        for (int i = 0; i < graphicsPipelineCreateInfo.VertexInputState.VertexBufferDescriptions.Length; i++)
        {
            vertexBindings[i] = graphicsPipelineCreateInfo.VertexInputState.VertexBufferDescriptions[i].ToNative();
        }

        int numColorTargets = graphicsPipelineCreateInfo.TargetInfo.ColorTargetDescriptions != null
            ? graphicsPipelineCreateInfo.TargetInfo.ColorTargetDescriptions.Length
            : 0;

        SDL_GPUColorTargetDescription* colorAttachmentDescriptions = stackalloc SDL_GPUColorTargetDescription[numColorTargets];

        for (int i = 0; i < numColorTargets; i++)
        {
            colorAttachmentDescriptions[i].format = (SDL_GPUTextureFormat)graphicsPipelineCreateInfo.TargetInfo.ColorTargetDescriptions![i].Format;
            colorAttachmentDescriptions[i].blend_state = graphicsPipelineCreateInfo.TargetInfo.ColorTargetDescriptions[i].BlendState.ToNative();
        }

        createInfo.vertex_shader = graphicsPipelineCreateInfo.VertexShader.Handle;
        createInfo.fragment_shader = graphicsPipelineCreateInfo.FragmentShader.Handle;

        createInfo.vertex_input_state.vertex_attributes = vertexAttributes;
        createInfo.vertex_input_state.num_vertex_attributes = (uint)graphicsPipelineCreateInfo.VertexInputState.VertexAttributes.Length;
        createInfo.vertex_input_state.vertex_buffer_descriptions = vertexBindings;
        createInfo.vertex_input_state.num_vertex_buffers = (uint)graphicsPipelineCreateInfo.VertexInputState.VertexBufferDescriptions.Length;

        createInfo.primitive_type = (SDL_GPUPrimitiveType)graphicsPipelineCreateInfo.PrimitiveType;
        createInfo.rasterizer_state = graphicsPipelineCreateInfo.RasterizerState.ToNative();
        createInfo.multisample_state = graphicsPipelineCreateInfo.MultisampleState.ToNative();
        createInfo.depth_stencil_state = graphicsPipelineCreateInfo.DepthStencilState.ToNative();

        createInfo.target_info = new()
        {
            num_color_targets = (uint)numColorTargets,
            color_target_descriptions = colorAttachmentDescriptions,
            depth_stencil_format = (SDL_GPUTextureFormat)graphicsPipelineCreateInfo.TargetInfo.DepthStencilFormat,
            has_depth_stencil_target = graphicsPipelineCreateInfo.TargetInfo.HasDepthStencilTarget
        };

        createInfo.props = (SDL_PropertiesID)graphicsPipelineCreateInfo.Props;

        SDL_GPUGraphicsPipeline* handle = SDL3.SDL_CreateGPUGraphicsPipeline(device.Handle, &createInfo);

        NativeMemory.Free(vertexAttributes);
        NativeMemory.Free(vertexBindings);

        if (handle == null)
        {
            throw new Exception("Could not create graphics pipeline!");
        }

        return new GraphicsPipeline(device, handle, graphicsPipelineCreateInfo);
    }

    protected override void Dispose(bool disposing)
    {
        if (Handle == null) return;
        SDL3.SDL_ReleaseGPUGraphicsPipeline(Device.Handle, Handle);
        Handle = null;
    }
}
