using System.Buffers.Binary;

namespace Beutl.Media.Decoding.APNG.Chunks;

// ReSharper disable once InconsistentNaming
public class acTLChunk : Chunk
{
    public acTLChunk(byte[] bytes)
        : base(bytes)
    {
    }

    public acTLChunk(MemoryStream ms)
        : base(ms)
    {
    }

    public acTLChunk(Chunk chunk)
        : base(chunk)
    {
    }

    public uint NumFrames { get; private set; }

    public uint NumPlays { get; private set; }

    protected override void ParseData(MemoryStream ms)
    {
        NumFrames = BinaryPrimitives.ReadUInt32BigEndian(ms.ReadBytes(4));
        NumPlays = BinaryPrimitives.ReadUInt32BigEndian(ms.ReadBytes(4));
    }
}
