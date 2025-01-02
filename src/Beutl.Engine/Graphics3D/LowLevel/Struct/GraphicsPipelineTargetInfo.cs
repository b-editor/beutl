namespace Beutl.Graphics3D;

public readonly struct GraphicsPipelineTargetInfo
{
    public ColorTargetDescription[] ColorTargetDescriptions { get; init; }

    public TextureFormat DepthStencilFormat { get; init; }

    public bool HasDepthStencilTarget { get; init; }
}
