using System.Runtime.InteropServices;

namespace Beutl.Media.Music.Samples;

[StructLayout(LayoutKind.Sequential)]
public struct Stereo16BitInteger(short left, short right) : ISample<Stereo16BitInteger>
{
    public short Left = left;

    public short Right = right;

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

    public static Stereo16BitInteger Amplifier(Stereo16BitInteger s, Sample level)
    {
        return new Stereo16BitInteger(
            left: (short)MathF.Round(s.Left * level.Left, MidpointRounding.AwayFromZero),
            right: (short)MathF.Round(s.Right * level.Right, MidpointRounding.AwayFromZero));
    }

    public static Stereo16BitInteger Compound(Stereo16BitInteger s1, Stereo16BitInteger s2)
    {
        return new Stereo16BitInteger((short)(s1.Left + s2.Left), (short)(s1.Right + s2.Right));
    }

    public static unsafe void GetChannelData(Stereo16BitInteger s, int channel, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        if (channel is 0 or 1)
        {
            var span = new Span<byte>(channel == 0 ? &s.Left : &s.Right, sizeof(short));
            span.CopyTo(destination);
            bytesWritten = span.Length;
        }
    }

    public static int GetNumChannels()
    {
        return 2;
    }
}
