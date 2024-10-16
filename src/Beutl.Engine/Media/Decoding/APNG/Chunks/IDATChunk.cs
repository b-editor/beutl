namespace Beutl.Media.Decoding.APNG.Chunks;

public class IDATChunk : Chunk
{
    public IDATChunk(byte[] bytes)
        : base(bytes)
    {
    }

    public IDATChunk(MemoryStream ms)
        : base(ms)
    {
    }

    public IDATChunk(Chunk chunk)
        : base(chunk)
    {
    }
}
