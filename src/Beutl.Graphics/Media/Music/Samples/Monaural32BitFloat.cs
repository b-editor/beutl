using System.Runtime.InteropServices;

namespace Beutl.Media.Music.Samples;

[StructLayout(LayoutKind.Sequential)]
public struct Monaural32BitFloat : ISample<Monaural32BitFloat>
{
    public float Value;

    public Monaural32BitFloat(float value)
    {
        Value = value;
    }

    public static Monaural32BitFloat ConvertFrom(Sample src)
    {
        return new Monaural32BitFloat(src.Left);
    }

    public static Sample ConvertTo(Monaural32BitFloat src)
    {
        return new Sample(src.Value, src.Value);
    }

    public Monaural32BitFloat Amplifier(Sample level)
    {
        return new Monaural32BitFloat(Value * level.Left);
    }

    public Monaural32BitFloat Compound(Monaural32BitFloat s)
    {
        return new Monaural32BitFloat(Value + s.Value);
    }
}
