using SDL;

namespace Beutl.Graphics3D;

public readonly struct TextureLocation
{
    public TextureLocation(Texture texture)
    {
        Texture = texture;
    }

    public Texture Texture { get; init; }

    public uint MipLevel { get; init; }

    public uint Layer { get; init; }

    public uint X { get; init; }

    public uint Y { get; init; }

    public uint Z { get; init; }

    internal unsafe SDL_GPUTextureLocation ToNative()
    {
        return new SDL_GPUTextureLocation
        {
            texture = Texture != null ? Texture.Handle : null,
            mip_level = MipLevel,
            layer = Layer,
            x = X,
            y = Y,
            z = Z
        };
    }
}
