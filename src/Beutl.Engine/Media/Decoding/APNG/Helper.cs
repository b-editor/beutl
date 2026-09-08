using System.Buffers.Binary;

namespace Beutl.Media.Decoding.APNG;

internal static class Helper
{
    internal static int ConvertEndian(int i) => BinaryPrimitives.ReverseEndianness(i);

    internal static uint ConvertEndian(uint i) => BinaryPrimitives.ReverseEndianness(i);

    internal static short ConvertEndian(short i) => BinaryPrimitives.ReverseEndianness(i);

    internal static ushort ConvertEndian(ushort i) => BinaryPrimitives.ReverseEndianness(i);

    public static bool IsBytesEqual(byte[] byte1, byte[] byte2)
    {
        if (byte1.Length != byte2.Length)
            return false;

        for (int i = 0; i < byte1.Length; i++)
        {
            if (byte1[i] != byte2[i])
                return false;
        }

        return true;
    }
}
