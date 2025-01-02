namespace Beutl.Graphics3D;

public readonly struct VertexInputState
{
    public static readonly VertexInputState Empty = new()
    {
        VertexBufferDescriptions = [],
        VertexAttributes = []
    };

    public VertexBufferDescription[] VertexBufferDescriptions { get; init; }

    public VertexAttribute[] VertexAttributes { get; init; }

    public static VertexInputState CreateSingleBinding<T>(uint slot = 0,
        VertexInputRate inputRate = VertexInputRate.Vertex, uint stepRate = 0, uint locationOffset = 0)
        where T : unmanaged, IVertexType
    {
        var description = VertexBufferDescription.Create<T>(slot, inputRate, stepRate);
        var attributes = new VertexAttribute[T.Formats.Length];

        for (uint i = 0; i < T.Formats.Length; i++)
        {
            attributes[i] = new VertexAttribute
            {
                BufferSlot = slot,
                Location = locationOffset + i,
                Format = T.Formats[i],
                Offset = T.Offsets[i]
            };
        }

        return new VertexInputState { VertexBufferDescriptions = [description], VertexAttributes = attributes };
    }
}
