using Beutl.Media.Decoding.APNG.Chunks;

namespace Beutl.Media.Decoding.APNG;

public class Frame
{
    public static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    ///     Gets or Sets the acTL chunk
    /// </summary>
    public IHDRChunk IHDRChunk { get; set; }

    /// <summary>
    ///     Gets or Sets the fcTL chunk
    /// </summary>
    public fcTLChunk? fcTLChunk { get; set; }

    /// <summary>
    ///     Gets or Sets the IEND chunk
    /// </summary>
    public IENDChunk IENDChunk { get; set; }

    /// <summary>
    ///     Gets or Sets the other chunks
    /// </summary>
    public List<OtherChunk> OtherChunks { get; set; } = [];

    /// <summary>
    ///     Gets or Sets the IDAT chunks
    /// </summary>
    public List<IDATChunk> IDATChunks { get; set; } = [];

    /// <summary>
    ///     Add an Chunk to end end of existing list.
    /// </summary>
    public void AddOtherChunk(OtherChunk chunk)
    {
        OtherChunks.Add(chunk);
    }

    /// <summary>
    ///     Add an IDAT Chunk to end end of existing list.
    /// </summary>
    public void AddIDATChunk(IDATChunk chunk)
    {
        IDATChunks.Add(chunk);
    }

    /// <summary>
    ///     Gets the frame as PNG FileStream.
    /// </summary>
    public MemoryStream GetStream()
    {
        var ihdrChunk = new IHDRChunk(IHDRChunk);
        if (fcTLChunk != null)
        {
            // Fix frame size with fcTL data.
            ihdrChunk.ModifyChunkData(0, Helper.ConvertEndian(fcTLChunk.Width));
            ihdrChunk.ModifyChunkData(4, Helper.ConvertEndian(fcTLChunk.Height));
        }

        // Write image data
        var ms = new MemoryStream();

        ms.WriteBytes(Signature);
        ms.WriteBytes(ihdrChunk.RawData);
        OtherChunks.ForEach(o => ms.WriteBytes(o.RawData));
        IDATChunks.ForEach(i => ms.WriteBytes(i.RawData));
        ms.WriteBytes(IENDChunk.RawData);

        ms.Flush();
        ms.Position = 0;
        return ms;
    }
}
