// ReSharper disable VirtualMemberCallInConstructor

namespace Beutl.Media.Decoding.APNG.Chunks;

public class Chunk
{
    internal Chunk(byte[] bytes)
    {
        var ms = new MemoryStream(bytes);
        Length = Helper.ConvertEndian(ms.ReadUInt32());
        ChunkType = System.Text.Encoding.ASCII.GetString(ms.ReadBytes(4));
        ChunkData = ms.ReadBytes((int)Length);
        Crc = Helper.ConvertEndian(ms.ReadUInt32());

        if (ms.Position != ms.Length)
            throw new Exception("Chunk length not correct.");
        if (Length != ChunkData.Length)
            throw new Exception("Chunk data length not correct.");

        ParseData(new MemoryStream(ChunkData));
    }

    internal Chunk(MemoryStream ms)
    {
        Length = Helper.ConvertEndian(ms.ReadUInt32());
        ChunkType = System.Text.Encoding.ASCII.GetString(ms.ReadBytes(4));
        ChunkData = ms.ReadBytes((int)Length);
        Crc = Helper.ConvertEndian(ms.ReadUInt32());

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

    public uint Length { get; set; }

    public string ChunkType { get; set; }

    public byte[] ChunkData { get; set; }

    public uint Crc { get; set; }

    public byte[] RawData
    {
        get
        {
            var ms = new MemoryStream();
            ms.WriteUInt32(Helper.ConvertEndian(Length));
            ms.WriteBytes(System.Text.Encoding.ASCII.GetBytes(ChunkType));
            ms.WriteBytes(ChunkData);

            ms.WriteUInt32(Helper.ConvertEndian(Crc));

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
