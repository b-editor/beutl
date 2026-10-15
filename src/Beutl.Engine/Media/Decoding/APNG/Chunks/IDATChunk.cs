namespace Beutl.Media.Decoding.APNG.Chunks;

public class IDATChunk : Chunk
{
    public IDATChunk(MemoryStream ms)
        : base(ms)
    {
    }

    public IDATChunk(Chunk chunk)
        : base(chunk)
    {
    }
}
