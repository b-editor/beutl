using SDL;

namespace Beutl.Graphics3D;

public readonly record struct BufferLocation(Buffer Buffer, uint Offset = 0)
{
    internal unsafe SDL_GPUBufferLocation ToNative()
    {
        return new SDL_GPUBufferLocation
        {
            buffer = Buffer != null ? Buffer.Handle : null,
            offset = Offset
        };
    }

    public static implicit operator BufferLocation(Buffer buffer) => new(buffer);
}
