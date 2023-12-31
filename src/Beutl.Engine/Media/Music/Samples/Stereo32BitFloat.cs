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

    public static Stereo32BitFloat Amplifier(Stereo32BitFloat s, Sample level)
    {
        return new Stereo32BitFloat(s.Left * level.Left, s.Right * level.Right);
    }

    public static Stereo32BitFloat Compound(Stereo32BitFloat s1, Stereo32BitFloat s2)
    {
        return new Stereo32BitFloat(s1.Left + s2.Left, s1.Right + s2.Right);
    }

    public static unsafe void GetChannelData(Stereo32BitFloat s, int channel, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        if (channel is 0 or 1)
        {
            var span = new Span<byte>(channel == 0 ? &s.Left : &s.Right, sizeof(float));
            span.CopyTo(destination);
            bytesWritten = span.Length;
        }
    }

    public static int GetNumChannels()
    {
        return 2;
    }

    public static implicit operator Vector2(Stereo32BitFloat s) => new(s.Left, s.Right);

    public static implicit operator Stereo32BitFloat(Vector2 v) => new(v.X, v.Y);
}
