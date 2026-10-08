namespace Beutl.Media.Decoding.APNG;

internal static class StreamExtensions
{
    #region Read

    public static byte[] ReadBytes(this Stream ms, int count)
    {
        var buffer = new byte[count];

        if (ms.Read(buffer, 0, count) != count)
            throw new Exception("End reached.");

        return buffer;
    }

    public static Int32 ReadInt32(this Stream ms)
    {
        return BitConverter.ToInt32(ReadBytes(ms, 4), 0);
    }

    public static UInt16 ReadUInt16(this Stream ms)
    {
        return BitConverter.ToUInt16(ReadBytes(ms, 2), 0);
    }

    public static UInt32 ReadUInt32(this Stream ms)
    {
        return BitConverter.ToUInt32(ReadBytes(ms, 4), 0);
    }

    #endregion Read

    #region Write

    public static void WriteBytes(this Stream ms, byte[] value)
    {
        ms.Write(value, 0, value.Length);
    }

    public static void WriteUInt32(this Stream ms, UInt32 value)
    {
        ms.Write(BitConverter.GetBytes(value), 0, 4);
    }

    #endregion Write
}
