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
