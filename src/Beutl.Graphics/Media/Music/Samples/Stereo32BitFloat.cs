using System.Runtime.InteropServices;

namespace Beutl.Media.Music.Samples;

[StructLayout(LayoutKind.Sequential)]
public struct Stereo32BitFloat : ISample<Stereo32BitFloat>
{
    public float Left;

    public float Right;

    public Stereo32BitFloat(float left, float right)
    {
        Left = left;
        Right = right;
    }

    public static Stereo32BitFloat ConvertFrom(Sample src)
    {
        return new Stereo32BitFloat(src.Left, src.Right);
    }
    public static Sample ConvertTo(Stereo32BitFloat src)
    {
        return new Sample(src.Left, src.Right);
    }

    public Stereo32BitFloat Amplifier(Sample level)
    {
        return new Stereo32BitFloat(Left * level.Left, Right * level.Right);
    }

    public Stereo32BitFloat Compound(Stereo32BitFloat s)
    {
        return new Stereo32BitFloat(Left + s.Left, Right + s.Right);
    }
}
