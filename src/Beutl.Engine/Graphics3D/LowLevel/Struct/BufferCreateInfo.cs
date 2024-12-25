using SDL;

namespace Beutl.Graphics3D;

public readonly struct BufferCreateInfo
{
    public BufferUsageFlags Usage { get; init; }

    public uint Size { get; init; }

    // public uint Props { get; init; }

    internal SDL_GPUBufferCreateInfo ToNative()
    {
        return new SDL_GPUBufferCreateInfo
        {
            usage = (SDL_GPUBufferUsageFlags)Usage,
            size = Size
        };
    }
}
