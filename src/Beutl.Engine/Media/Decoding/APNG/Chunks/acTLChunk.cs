using System.Buffers.Binary;

namespace Beutl.Media.Decoding.APNG.Chunks;

// ReSharper disable once InconsistentNaming
public class acTLChunk : Chunk
{
    public acTLChunk(Chunk chunk)
        : base(chunk)
    {
    }

    public uint NumPlays { get; private set; }

    protected override void ParseData(MemoryStream ms)
    {
        // Skips num_frames; the frame list comes from the fcTL chunks.
        ms.Seek(4, SeekOrigin.Current);
        NumPlays = BinaryPrimitives.ReadUInt32BigEndian(ms.ReadBytes(4));
    }
}
