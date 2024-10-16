namespace Beutl.Media.Decoding.APNG.Chunks;

// ReSharper disable once InconsistentNaming
public class fcTLChunk : Chunk
{
    public fcTLChunk(byte[] bytes)
        : base(bytes)
    {
    }

    public fcTLChunk(MemoryStream ms)
        : base(ms)
    {
    }

    public fcTLChunk(Chunk chunk)
        : base(chunk)
    {
    }

    /// <summary>
    ///     Sequence number of the animation chunk, starting from 0
    /// </summary>
    public uint SequenceNumber { get; private set; }

    /// <summary>
    ///     Width of the following frame
    /// </summary>
    public uint Width { get; private set; }

    /// <summary>
    ///     Height of the following frame
    /// </summary>
    public uint Height { get; private set; }

    /// <summary>
    ///     X position at which to render the following frame
    /// </summary>
    public uint XOffset { get; private set; }

    /// <summary>
    ///     Y position at which to render the following frame
    /// </summary>
    public uint YOffset { get; private set; }

    /// <summary>
    ///     Frame delay fraction numerator
    /// </summary>
    public ushort DelayNum { get; private set; }

    /// <summary>
    ///     Frame delay fraction denominator
    /// </summary>
    public ushort DelayDen { get; private set; }

    /// <summary>
    ///     Type of frame area disposal to be done after rendering this frame
    /// </summary>
    public DisposeOps DisposeOp { get; private set; }

    /// <summary>
    ///     Type of frame area rendering for this frame
    /// </summary>
    public BlendOps BlendOp { get; private set; }

    protected override void ParseData(MemoryStream ms)
    {
        SequenceNumber = Helper.ConvertEndian(ms.ReadUInt32());
        Width = Helper.ConvertEndian(ms.ReadUInt32());
        Height = Helper.ConvertEndian(ms.ReadUInt32());
        XOffset = Helper.ConvertEndian(ms.ReadUInt32());
        YOffset = Helper.ConvertEndian(ms.ReadUInt32());
        DelayNum = Helper.ConvertEndian(ms.ReadUInt16());
        DelayDen = Helper.ConvertEndian(ms.ReadUInt16());
        DisposeOp = (DisposeOps)ms.ReadByte();
        BlendOp = (BlendOps)ms.ReadByte();
    }
}
