using SDL;

namespace Beutl.Graphics3D;

public readonly struct VertexAttribute
{
    public uint Location { get; init; }

    public uint BufferSlot { get; init; }

    public VertexElementFormat Format { get; init; }

    public uint Offset { get; init; }

    internal unsafe SDL_GPUVertexAttribute ToNative()
    {
        return new SDL_GPUVertexAttribute
        {
            location = Location,
            buffer_slot = BufferSlot,
            format = (SDL_GPUVertexElementFormat)Format,
            offset = Offset
        };
    }
}
