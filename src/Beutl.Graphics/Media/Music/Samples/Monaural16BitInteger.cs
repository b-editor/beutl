using System.Runtime.InteropServices;

namespace Beutl.Media.Music.Samples;

[StructLayout(LayoutKind.Sequential)]
public struct Monaural16BitInteger : ISample<Monaural16BitInteger>
{
    public short Value;

    public Monaural16BitInteger(short value)
    {
        Value = value;
    }

    public static Monaural16BitInteger ConvertFrom(Sample src)
    {
        return new Monaural16BitInteger((short)MathF.Round(src.Left * short.MaxValue, MidpointRounding.AwayFromZero));
    }

    public static Sample ConvertTo(Monaural16BitInteger src)
    {
        float v = (float)src.Value / short.MaxValue;
        return new Sample(v, v);
    }

    public Monaural16BitInteger Amplifier(Sample level)
    {
        return new Monaural16BitInteger(
            (short)MathF.Round(Value * level.Left, MidpointRounding.AwayFromZero));
    }

    public Monaural16BitInteger Compound(Monaural16BitInteger s)
    {
        return new Monaural16BitInteger((short)(Value + s.Value));
    }
}
