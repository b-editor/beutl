namespace Beutl.Media.Decoding.APNG.Chunks;

public class IHDRChunk : Chunk
{
    public IHDRChunk(byte[] chunkBytes)
        : base(chunkBytes)
    {
    }

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

    public byte BitDepth { get; private set; }

    public byte ColorType { get; private set; }

    public byte CompressionMethod { get; private set; }

    public byte FilterMethod { get; private set; }

    public byte InterlaceMethod { get; private set; }

    protected override void ParseData(MemoryStream ms)
    {
        Width = Helper.ConvertEndian(ms.ReadInt32());
        Height = Helper.ConvertEndian(ms.ReadInt32());
        BitDepth = Convert.ToByte(ms.ReadByte());
        ColorType = Convert.ToByte(ms.ReadByte());
        CompressionMethod = Convert.ToByte(ms.ReadByte());
        FilterMethod = Convert.ToByte(ms.ReadByte());
        InterlaceMethod = Convert.ToByte(ms.ReadByte());
    }
}
