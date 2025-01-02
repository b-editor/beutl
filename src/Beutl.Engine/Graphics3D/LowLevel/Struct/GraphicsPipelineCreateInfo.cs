namespace Beutl.Graphics3D;

public readonly struct GraphicsPipelineCreateInfo
{
    public Shader VertexShader { get; init; }

    public Shader FragmentShader { get; init; }

    public VertexInputState VertexInputState { get; init; }

    public PrimitiveType PrimitiveType { get; init; }

    public RasterizerState RasterizerState { get; init; }

    public MultisampleState MultisampleState { get; init; }

    public DepthStencilState DepthStencilState { get; init; }

    public GraphicsPipelineTargetInfo TargetInfo { get; init; }

    public uint Props { get; init; }
}
