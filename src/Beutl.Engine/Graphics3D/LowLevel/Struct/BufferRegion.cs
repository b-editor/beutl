using SDL;

namespace Beutl.Graphics3D;

public readonly record struct BufferRegion(Buffer Buffer, uint Offset, uint Size)
{
    public BufferRegion(Buffer buffer, uint offset = 0)
        : this(buffer, offset, buffer.Size - offset)
    {
    }

    internal unsafe SDL_GPUBufferRegion ToNative()
    {
        return new SDL_GPUBufferRegion { buffer = Buffer != null ? Buffer.Handle : null, offset = Offset, size = Size };
    }

    public static implicit operator BufferRegion(Buffer buffer) => new()
    {
        Buffer = buffer, Offset = 0, Size = buffer.Size
    };
}
