using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Beutl.Media.Music.Samples;

[StructLayout(LayoutKind.Sequential)]
public struct Monaural16BitInteger(short value) : ISample<Monaural16BitInteger>
{
    public short Value = value;

    public static Monaural16BitInteger ConvertFrom(Sample src)
    {
        return new Monaural16BitInteger((short)MathF.Round(src.Left * short.MaxValue, MidpointRounding.AwayFromZero));
    }

    public static Sample ConvertTo(Monaural16BitInteger src)
    {
        float v = (float)src.Value / short.MaxValue;
        return new Sample(v, v);
    }

    public static Monaural16BitInteger Amplifier(Monaural16BitInteger s, Sample level)
    {
        return new Monaural16BitInteger(
            (short)MathF.Round(s.Value * level.Left, MidpointRounding.AwayFromZero));
    }

    public static Monaural16BitInteger Compound(Monaural16BitInteger s1, Monaural16BitInteger s2)
    {
        return new Monaural16BitInteger((short)(s1.Value + s2.Value));
    }

    public static int GetNumChannels()
    {
        return 1;
    }

    public static unsafe void GetChannelData(Monaural16BitInteger s, int channel, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        if (channel == 0)
        {
            var span = new Span<byte>(&s, sizeof(Monaural16BitInteger));
            span.CopyTo(destination);
            bytesWritten = span.Length;
        }
    }
}
