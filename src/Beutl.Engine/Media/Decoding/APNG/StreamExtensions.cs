namespace Beutl.Media.Decoding.APNG;

internal static class StreamExtensions
{
    #region Peek

    public static byte[] PeekBytes(this Stream ms, int position, int count)
    {
        long prevPosition = ms.Position;

        ms.Position = position;
        byte[] buffer = ReadBytes(ms, count);
        ms.Position = prevPosition;

        return buffer;
    }

    public static char PeekChar(this Stream ms)
    {
        return PeekChar(ms, (int)ms.Position);
    }

    public static char PeekChar(this Stream ms, int position)
    {
        return BitConverter.ToChar(PeekBytes(ms, position, 2), 0);
    }

    public static Int16 PeekInt16(this Stream ms)
    {
        return PeekInt16(ms, (int)ms.Position);
    }

    public static Int16 PeekInt16(this Stream ms, int position)
    {
        return BitConverter.ToInt16(PeekBytes(ms, position, 2), 0);
    }

    public static Int32 PeekInt32(this Stream ms)
    {
        return PeekInt32(ms, (int)ms.Position);
    }

    public static Int32 PeekInt32(this Stream ms, int position)
    {
        return BitConverter.ToInt32(PeekBytes(ms, position, 4), 0);
    }

    public static Int64 PeekInt64(this Stream ms)
    {
        return PeekInt64(ms, (int)ms.Position);
    }

    public static Int64 PeekInt64(this Stream ms, int position)
    {
        return BitConverter.ToInt64(PeekBytes(ms, position, 8), 0);
    }

    public static UInt16 PeekUInt16(this Stream ms)
    {
        return PeekUInt16(ms, (int)ms.Position);
    }

    public static UInt16 PeekUInt16(this Stream ms, int position)
    {
        return BitConverter.ToUInt16(PeekBytes(ms, position, 2), 0);
    }

    public static UInt32 PeekUInt32(this Stream ms)
    {
        return PeekUInt32(ms, (int)ms.Position);
    }

    public static UInt32 PeekUInt32(this Stream ms, int position)
    {
        return BitConverter.ToUInt32(PeekBytes(ms, position, 4), 0);
    }

    public static UInt64 PeekUInt64(this Stream ms)
    {
        return PeekUInt64(ms, (int)ms.Position);
    }

    public static UInt64 PeekUInt64(this Stream ms, int position)
    {
        return BitConverter.ToUInt64(PeekBytes(ms, position, 8), 0);
    }

    #endregion Peek

    #region Read

    public static byte[] ReadBytes(this Stream ms, int count)
    {
        var buffer = new byte[count];

        if (ms.Read(buffer, 0, count) != count)
            throw new Exception("End reached.");

        return buffer;
    }

    public static char ReadChar(this Stream ms)
    {
        return BitConverter.ToChar(ReadBytes(ms, 2), 0);
    }

    public static Int16 ReadInt16(this Stream ms)
    {
        return BitConverter.ToInt16(ReadBytes(ms, 2), 0);
    }

    public static Int32 ReadInt32(this Stream ms)
    {
        return BitConverter.ToInt32(ReadBytes(ms, 4), 0);
    }

    public static Int64 ReadInt64(this Stream ms)
    {
        return BitConverter.ToInt64(ReadBytes(ms, 8), 0);
    }

    public static UInt16 ReadUInt16(this Stream ms)
    {
        return BitConverter.ToUInt16(ReadBytes(ms, 2), 0);
    }

    public static UInt32 ReadUInt32(this Stream ms)
    {
        return BitConverter.ToUInt32(ReadBytes(ms, 4), 0);
    }

    public static UInt64 ReadUInt64(this Stream ms)
    {
        return BitConverter.ToUInt64(ReadBytes(ms, 8), 0);
    }

    #endregion Read

    #region Write

    public static void WriteByte(this Stream ms, int position, byte value)
    {
        long prevPosition = ms.Position;

        ms.Position = position;
        ms.WriteByte(value);
        ms.Position = prevPosition;
    }

    public static void WriteBytes(this Stream ms, byte[] value)
    {
        ms.Write(value, 0, value.Length);
    }

    public static void WriteBytes(this Stream ms, int position, byte[] value)
    {
        long prevPosition = ms.Position;

        ms.Position = position;
        ms.Write(value, 0, value.Length);
        ms.Position = prevPosition;
    }

    public static void WriteInt16(this Stream ms, Int16 value)
    {
        ms.Write(BitConverter.GetBytes(value), 0, 2);
    }

    public static void WriteInt16(this Stream ms, int position, Int16 value)
    {
        WriteBytes(ms, position, BitConverter.GetBytes(value));
    }

    public static void WriteInt32(this Stream ms, Int32 value)
    {
        ms.Write(BitConverter.GetBytes(value), 0, 4);
    }

    public static void WriteInt32(this Stream ms, int position, Int32 value)
    {
        WriteBytes(ms, position, BitConverter.GetBytes(value));
    }

    public static void WriteInt64(this Stream ms, Int64 value)
    {
        ms.Write(BitConverter.GetBytes(value), 0, 8);
    }

    public static void WriteInt64(this Stream ms, int position, Int64 value)
    {
        WriteBytes(ms, position, BitConverter.GetBytes(value));
    }

    public static void WriteUInt16(this Stream ms, UInt16 value)
    {
        ms.Write(BitConverter.GetBytes(value), 0, 2);
    }

    public static void WriteUInt16(this Stream ms, int position, UInt16 value)
    {
        WriteBytes(ms, position, BitConverter.GetBytes(value));
    }

    public static void WriteUInt32(this Stream ms, UInt32 value)
    {
        ms.Write(BitConverter.GetBytes(value), 0, 4);
    }

    public static void WriteUInt32(this Stream ms, int position, UInt32 value)
    {
        WriteBytes(ms, position, BitConverter.GetBytes(value));
    }

    public static void WriteUInt64(this Stream ms, UInt64 value)
    {
        ms.Write(BitConverter.GetBytes(value), 0, 8);
    }

    public static void WriteUInt64(this Stream ms, int position, UInt64 value)
    {
        WriteBytes(ms, position, BitConverter.GetBytes(value));
    }

    #endregion Write
}
