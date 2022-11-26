using System.Runtime.InteropServices;

namespace Beutl.Media.Music.Samples;

[StructLayout(LayoutKind.Sequential)]
public struct Stereo32BitInteger : ISample<Stereo32BitInteger>
{
    public int Left;

    public int Right;

    public Stereo32BitInteger(int left, int right)
    {
        Left = left;
        Right = right;
    }

    public static Stereo32BitInteger ConvertFrom(Sample src)
    {
        return new Stereo32BitInteger(
            left: (int)MathF.Round(src.Left * int.MaxValue, MidpointRounding.AwayFromZero),
            right: (int)MathF.Round(src.Right * int.MaxValue, MidpointRounding.AwayFromZero));
    }

    public static Sample ConvertTo(Stereo32BitInteger src)
    {
        return new Sample((float)src.Left / int.MaxValue, (float)src.Right / int.MaxValue);
    }

    public Stereo32BitInteger Amplifier(Sample level)
    {
        return new Stereo32BitInteger(
            left: (int)MathF.Round(Left * level.Left, MidpointRounding.AwayFromZero),
            right: (int)MathF.Round(Right * level.Right, MidpointRounding.AwayFromZero));
    }

    public Stereo32BitInteger Compound(Stereo32BitInteger s)
    {
        return new Stereo32BitInteger(Left + s.Left, Right + s.Right);
    }
}
