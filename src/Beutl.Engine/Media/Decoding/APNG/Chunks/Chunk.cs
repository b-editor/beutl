// ReSharper disable VirtualMemberCallInConstructor

using System.Buffers.Binary;

namespace Beutl.Media.Decoding.APNG.Chunks;

public class Chunk
{
    internal Chunk(MemoryStream ms)
    {
        Length = BinaryPrimitives.ReadUInt32BigEndian(ms.ReadBytes(4));
        ChunkType = System.Text.Encoding.ASCII.GetString(ms.ReadBytes(4));
        ChunkData = ms.ReadBytes((int)Length);
        Crc = BinaryPrimitives.ReadUInt32BigEndian(ms.ReadBytes(4));

        ParseData(new MemoryStream(ChunkData));
    }

    internal Chunk(Chunk chunk)
    {
        Length = chunk.Length;
        ChunkType = chunk.ChunkType;
        ChunkData = chunk.ChunkData;
        Crc = chunk.Crc;
        ParseData(new MemoryStream(ChunkData));
    }

    public uint Length { get; }

    public string ChunkType { get; }

    public byte[] ChunkData { get; }

    public uint Crc { get; set; }

    public byte[] RawData
    {
        get
        {
            var ms = new MemoryStream();
            ms.WriteUInt32(BinaryPrimitives.ReverseEndianness(Length));
            ms.WriteBytes(System.Text.Encoding.ASCII.GetBytes(ChunkType));
            ms.WriteBytes(ChunkData);

            ms.WriteUInt32(BinaryPrimitives.ReverseEndianness(Crc));

            return ms.ToArray();
        }
    }

    public void ModifyChunkData(int position, byte[] newData)
    {
        Array.Copy(newData, 0, ChunkData, position, newData.Length);

        using (var msCrc = new MemoryStream())
        {
            msCrc.WriteBytes(System.Text.Encoding.ASCII.GetBytes(ChunkType));
            msCrc.WriteBytes(ChunkData);

            Crc = CrcHelper.Calculate(msCrc.ToArray());
        }
    }

    /// <summary>
    ///     Modify the ChunkData part.
    /// </summary>
    public void ModifyChunkData(int position, uint newData)
    {
        ModifyChunkData(position, BitConverter.GetBytes(newData));
    }

    protected virtual void ParseData(MemoryStream ms)
    {
    }
}
