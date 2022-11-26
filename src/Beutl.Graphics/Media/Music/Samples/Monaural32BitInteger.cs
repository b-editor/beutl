using System.Runtime.InteropServices;

namespace Beutl.Media.Music.Samples;

[StructLayout(LayoutKind.Sequential)]
public struct Monaural32BitInteger : ISample<Monaural32BitInteger>
{
    public int Value;

    public Monaural32BitInteger(int value)
    {
        Value = value;
    }

    public static Monaural32BitInteger ConvertFrom(Sample src)
    {
        return new Monaural32BitInteger((int)MathF.Round(src.Left * int.MaxValue, MidpointRounding.AwayFromZero));
    }

    public static Sample ConvertTo(Monaural32BitInteger src)
    {
        float v = (float)src.Value / int.MaxValue;
        return new Sample(v, v);
    }

    public Monaural32BitInteger Amplifier(Sample level)
    {
        return new Monaural32BitInteger((int)MathF.Round(Value * level.Left, MidpointRounding.AwayFromZero));
    }

    public Monaural32BitInteger Compound(Monaural32BitInteger s)
    {
        return new Monaural32BitInteger(Value + s.Value);
    }
}
