using SDL;

namespace Beutl.Graphics3D;

public readonly struct BlitRegion
{
    public BlitRegion(Texture texture)
    {
        Texture = texture;
        Width = texture.Width;
        Height = texture.Height;
    }

    public Texture Texture { get; init; }

    public uint MipLevel { get; init; }

    public uint LayerOrDepthPlane { get; init; }

    public uint X { get; init; }

    public uint Y { get; init; }

    public uint Width { get; init; }

    public uint Height { get; init; }

    internal unsafe SDL_GPUBlitRegion ToNative()
    {
        return new SDL_GPUBlitRegion
        {
            texture = Texture == null ? null : Texture.Handle,
            mip_level = MipLevel,
            layer_or_depth_plane = LayerOrDepthPlane,
            x = X,
            y = Y,
            w = Width,
            h = Height
        };
    }
}

public struct BlitInfo
{
    public BlitRegion Source;
    public BlitRegion Destination;
    public LoadOp LoadOp;
    public ColorF ClearColor;
    public FlipMode FlipMode;
    public Filter Filter;
    public SDLBool Cycle;
}
