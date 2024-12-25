using SDL;

namespace Beutl.Graphics3D;

public readonly struct TextureRegion
{
    public TextureRegion(Texture texture)
    {
        Texture = texture;
        Width = texture.Width;
        Height = texture.Height;
        Depth = texture.Type == TextureType.ThreeDimensional ? texture.LayerCountOrDepth : 1;
    }

    public Texture Texture { get; init; }

    public uint MipLevel { get; init; }

    public uint Layer { get; init; }

    public uint X { get; init; }

    public uint Y { get; init; }

    public uint Z { get; init; }

    public uint Width { get; init; }

    public uint Height { get; init; }

    public uint Depth { get; init; }

    internal unsafe SDL_GPUTextureRegion ToNative()
    {
        return new SDL_GPUTextureRegion
        {
            texture = Texture.Handle,
            mip_level = MipLevel,
            layer = Layer,
            x = X,
            y = Y,
            z = Z,
            w = Width,
            h = Height,
            d = Depth
        };
    }
}
