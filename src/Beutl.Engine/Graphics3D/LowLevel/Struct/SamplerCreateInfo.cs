using SDL;

namespace Beutl.Graphics3D;

public readonly record struct SamplerCreateInfo
{
    public static readonly SamplerCreateInfo AnisotropicClamp = new()
    {
        MinFilter = Filter.Linear,
        MagFilter = Filter.Linear,
        MipmapMode = SamplerMipmapMode.Linear,
        AddressModeU = SamplerAddressMode.ClampToEdge,
        AddressModeV = SamplerAddressMode.ClampToEdge,
        AddressModeW = SamplerAddressMode.ClampToEdge,
        EnableAnisotropy = true,
        MaxAnisotropy = 4,
        MipLodBias = 0f,
        MinLod = 0,
        MaxLod = 1000 /* VK_LOD_CLAMP_NONE */
    };

    public static readonly SamplerCreateInfo AnisotropicWrap = new()
    {
        MinFilter = Filter.Linear,
        MagFilter = Filter.Linear,
        MipmapMode = SamplerMipmapMode.Linear,
        AddressModeU = SamplerAddressMode.Repeat,
        AddressModeV = SamplerAddressMode.Repeat,
        AddressModeW = SamplerAddressMode.Repeat,
        EnableAnisotropy = true,
        MaxAnisotropy = 4,
        MaxLod = 1000 /* VK_LOD_CLAMP_NONE */
    };

    public static readonly SamplerCreateInfo LinearClamp = new()
    {
        MinFilter = Filter.Linear,
        MagFilter = Filter.Linear,
        MipmapMode = SamplerMipmapMode.Linear,
        AddressModeU = SamplerAddressMode.ClampToEdge,
        AddressModeV = SamplerAddressMode.ClampToEdge,
        AddressModeW = SamplerAddressMode.ClampToEdge,
        MaxLod = 1000
    };

    public static readonly SamplerCreateInfo LinearWrap = new()
    {
        MinFilter = Filter.Linear,
        MagFilter = Filter.Linear,
        MipmapMode = SamplerMipmapMode.Linear,
        AddressModeU = SamplerAddressMode.Repeat,
        AddressModeV = SamplerAddressMode.Repeat,
        AddressModeW = SamplerAddressMode.Repeat,
        MaxLod = 1000
    };

    public static readonly SamplerCreateInfo PointClamp = new()
    {
        MinFilter = Filter.Nearest,
        MagFilter = Filter.Nearest,
        MipmapMode = SamplerMipmapMode.Nearest,
        AddressModeU = SamplerAddressMode.ClampToEdge,
        AddressModeV = SamplerAddressMode.ClampToEdge,
        AddressModeW = SamplerAddressMode.ClampToEdge,
        MaxLod = 1000
    };

    public static readonly SamplerCreateInfo PointWrap = new()
    {
        MinFilter = Filter.Nearest,
        MagFilter = Filter.Nearest,
        MipmapMode = SamplerMipmapMode.Nearest,
        AddressModeU = SamplerAddressMode.Repeat,
        AddressModeV = SamplerAddressMode.Repeat,
        AddressModeW = SamplerAddressMode.Repeat,
        MaxLod = 1000
    };

    public Filter MinFilter { get; init; }

    public Filter MagFilter { get; init; }

    public SamplerMipmapMode MipmapMode { get; init; }

    public SamplerAddressMode AddressModeU { get; init; }

    public SamplerAddressMode AddressModeV { get; init; }

    public SamplerAddressMode AddressModeW { get; init; }

    public float MipLodBias { get; init; }

    public float MaxAnisotropy { get; init; }

    public CompareOp CompareOp { get; init; }

    public float MinLod { get; init; }

    public float MaxLod { get; init; }

    public bool EnableAnisotropy { get; init; }

    public bool EnableCompare { get; init; }

    public uint Props { get; init; }

    internal SDL_GPUSamplerCreateInfo ToNative()
    {
        return new SDL_GPUSamplerCreateInfo
        {
            min_filter = (SDL_GPUFilter)MinFilter,
            mag_filter = (SDL_GPUFilter)MagFilter,
            mipmap_mode = (SDL_GPUSamplerMipmapMode)MipmapMode,
            address_mode_u = (SDL_GPUSamplerAddressMode)AddressModeU,
            address_mode_v = (SDL_GPUSamplerAddressMode)AddressModeV,
            address_mode_w = (SDL_GPUSamplerAddressMode)AddressModeW,
            mip_lod_bias = MipLodBias,
            max_anisotropy = MaxAnisotropy,
            compare_op = (SDL_GPUCompareOp)CompareOp,
            min_lod = MinLod,
            max_lod = MaxLod,
            enable_anisotropy = EnableAnisotropy,
            enable_compare = EnableCompare,
            props = (SDL_PropertiesID)Props
        };
    }
}
