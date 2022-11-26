using System.Runtime.InteropServices;

namespace Beutl.Media.Music.Samples;

[StructLayout(LayoutKind.Sequential)]
public struct Stereo16BitInteger : ISample<Stereo16BitInteger>
{
    public short Left;

    public short Right;

    public Stereo16BitInteger(short left, short right)
    {
        Left = left;
        Right = right;
    }

    public static Stereo16BitInteger ConvertFrom(Sample src)
    {
        return new Stereo16BitInteger(
            left: (short)MathF.Round(src.Left * short.MaxValue, MidpointRounding.AwayFromZero),
            right: (short)MathF.Round(src.Right * short.MaxValue, MidpointRounding.AwayFromZero));
    }

    public static Sample ConvertTo(Stereo16BitInteger src)
    {
        return new Sample((float)src.Left / short.MaxValue, (float)src.Right / short.MaxValue);
    }

    public Stereo16BitInteger Amplifier(Sample level)
    {
        return new Stereo16BitInteger(
            left: (short)MathF.Round(Left * level.Left, MidpointRounding.AwayFromZero),
            right: (short)MathF.Round(Right * level.Right, MidpointRounding.AwayFromZero));
    }

    public Stereo16BitInteger Compound(Stereo16BitInteger s)
    {
        return new Stereo16BitInteger((short)(Left + s.Left), (short)(Right + s.Right));
    }
}
