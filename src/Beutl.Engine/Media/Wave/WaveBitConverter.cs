namespace Beutl.Media.Wave;

internal static class WaveBitConverter
{
    public static short ToInt16(ReadOnlySpan<byte> value)
    {
        return (short)(value[1] << 8 | value[0]);
    }

    public static int ToInt24(ReadOnlySpan<byte> value)
    {
        return value[2] << 16 | value[1] << 8 | value[0];
    }

    public static uint ToUInt24(ReadOnlySpan<byte> value)
    {
        return (uint)(value[2] << 16 | value[1] << 8 | value[0]);
    }

    public static int ToInt32(ReadOnlySpan<byte> value)
    {
        return value[3] << 24 | value[2] << 16 | value[1] << 8 | value[0];
    }

    public static float ToSingle(ReadOnlySpan<byte> value)
    {
        return BitConverter.ToSingle(value);
    }

    public static ushort ToUInt16(ReadOnlySpan<byte> value)
    {
        return (ushort)(value[1] << 8 | value[0]);
    }

    // Uint32からInt32へ 
    // ※0 ～ 4294967296 から -2147483648 ～ 2147483647へ
    public static int ShiftInt32(uint x)
    {
        if (2147483648 <= x)
        {
            return (int)-(4294967296 - x);
        }
        else
        {
            return (int)x;
        }
    }

    public static sbyte ShiftInt8(byte x)
    {
        if (128 == x) return 0;

        if (129 >= x)
        {
            return (sbyte)(x - 128);
        }
        else
        {
            return (sbyte)-(128 - x);
        }
    }

    public static short ShiftInt16(ushort x)
    {
        if (32768 <= x)
        {
            return (short)-(65536 - x);
        }
        else
        {
            return (short)x;
        }
    }

    public static int ShiftInt24(uint x)
    {
        if (8388608 <= x)
        {
            return (int)-(16777216 - x);
        }
        else
        {
            return (int)x;
        }
    }
}
