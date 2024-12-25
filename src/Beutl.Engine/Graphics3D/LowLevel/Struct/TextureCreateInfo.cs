using SDL;

namespace Beutl.Graphics3D;

public readonly struct TextureCreateInfo
{
    public TextureType Type { get; init; }

    public TextureFormat Format { get; init; }

    public TextureUsageFlags Usage { get; init; }

    public uint Width { get; init; }

    public uint Height { get; init; }

    public uint LayerCountOrDepth { get; init; }

    public uint LevelCount { get; init; }

    public SampleCount SampleCount { get; init; }

    // public SDL_PropertiesID Props { get; init; }

    internal SDL_GPUTextureCreateInfo ToNative()
    {
        return new SDL_GPUTextureCreateInfo
        {
            type = (SDL_GPUTextureType)Type,
            format = (SDL_GPUTextureFormat)Format,
            usage = (SDL_GPUTextureUsageFlags)Usage,
            width = Width,
            height = Height,
            layer_count_or_depth = LayerCountOrDepth,
            num_levels = LevelCount,
            sample_count = (SDL_GPUSampleCount)SampleCount,
            // props = Props
        };
    }
}
