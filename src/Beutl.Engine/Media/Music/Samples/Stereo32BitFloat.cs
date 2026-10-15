using System.Numerics;
using System.Runtime.InteropServices;

namespace Beutl.Media.Music.Samples;

[StructLayout(LayoutKind.Sequential)]
public struct Stereo32BitFloat(float left, float right) : ISample<Stereo32BitFloat>
{
    public float Left = left;

    public float Right = right;

    public static Stereo32BitFloat ConvertFrom(Sample src)
    {
        return new Stereo32BitFloat(src.Left, src.Right);
    }
    public static Sample ConvertTo(Stereo32BitFloat src)
    {
        return new Sample(src.Left, src.Right);
    }

    public static int GetNumChannels()
    {
        return 2;
    }

    public static implicit operator Vector2(Stereo32BitFloat s) => new(s.Left, s.Right);

    public static implicit operator Stereo32BitFloat(Vector2 v) => new(v.X, v.Y);
}
