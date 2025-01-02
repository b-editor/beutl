namespace Beutl.Graphics3D;

public readonly struct ColorTargetDescription
{
    public TextureFormat Format { get; init; }

    public ColorTargetBlendState BlendState { get; init; }
}
