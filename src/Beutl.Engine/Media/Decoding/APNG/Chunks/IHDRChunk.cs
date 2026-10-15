using System.Buffers.Binary;

namespace Beutl.Media.Decoding.APNG.Chunks;

public class IHDRChunk : Chunk
{
    public IHDRChunk(MemoryStream ms)
        : base(ms)
    {
    }

    public IHDRChunk(Chunk chunk)
        : base(chunk)
    {
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    protected override void ParseData(MemoryStream ms)
    {
        Width = BinaryPrimitives.ReadInt32BigEndian(ms.ReadBytes(4));
        Height = BinaryPrimitives.ReadInt32BigEndian(ms.ReadBytes(4));
    }
}
