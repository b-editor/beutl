namespace Beutl.Media.Decoding.APNG.Chunks;

public class OtherChunk : Chunk
{
    public OtherChunk(byte[] bytes)
        : base(bytes)
    {
    }

    public OtherChunk(MemoryStream ms)
        : base(ms)
    {
    }

    public OtherChunk(Chunk chunk)
        : base(chunk)
    {
    }

    protected override void ParseData(MemoryStream ms)
    {
    }
}
