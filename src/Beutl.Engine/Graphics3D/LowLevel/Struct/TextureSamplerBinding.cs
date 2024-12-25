using SDL;
using Silk.NET.OpenGL;

namespace Beutl.Graphics3D;

public readonly record struct TextureSamplerBinding(Texture Texture, Sampler Sampler)
{
    internal unsafe SDL_GPUTextureSamplerBinding ToNative()
    {
        return new SDL_GPUTextureSamplerBinding
        {
            texture = Texture != null ? Texture.Handle : null,
            sampler = Sampler != null ? Sampler.Handle : null
        };
    }
}
