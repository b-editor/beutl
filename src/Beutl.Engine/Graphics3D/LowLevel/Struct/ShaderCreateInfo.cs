using SDL;

namespace Beutl.Graphics3D;

public readonly record struct ShaderCreateInfo
{
    public ShaderFormat Format { get; init; }

    public ShaderStage Stage { get; init; }

    public uint NumSamplers { get; init; }

    public uint NumStorageTextures { get; init; }

    public uint NumStorageBuffers { get; init; }

    public uint NumUniformBuffers { get; init; }

    public uint Props { get; init; }

    internal SDL_GPUShaderCreateInfo ToNative()
    {
        return new SDL_GPUShaderCreateInfo
        {
            format = (SDL_GPUShaderFormat)Format,
            stage = (SDL_GPUShaderStage)Stage,
            num_samplers = NumSamplers,
            num_storage_textures = NumStorageTextures,
            num_storage_buffers = NumStorageBuffers,
            num_uniform_buffers = NumUniformBuffers,
            props = (SDL_PropertiesID)Props
        };
    }
}
