namespace Beutl.Media.Decoding.APNG.Chunks;

public class IENDChunk : Chunk
{
    public IENDChunk(byte[] bytes)
        : base(bytes)
    {
    }

    public IENDChunk(MemoryStream ms)
        : base(ms)
    {
    }

    public IENDChunk(Chunk chunk)
        : base(chunk)
    {
    }
}
