using System.Runtime.InteropServices;

namespace Beutl.Media.Music.Samples;

[StructLayout(LayoutKind.Sequential)]
public struct Stereo32BitInteger(int left, int right) : ISample<Stereo32BitInteger>
{
    public int Left = left;

    public int Right = right;

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

    public static Stereo32BitInteger Amplifier(Stereo32BitInteger s, Sample level)
    {
        return new Stereo32BitInteger(
            left: (int)MathF.Round(s.Left * level.Left, MidpointRounding.AwayFromZero),
            right: (int)MathF.Round(s.Right * level.Right, MidpointRounding.AwayFromZero));
    }

    public static Stereo32BitInteger Compound(Stereo32BitInteger s1, Stereo32BitInteger s2)
    {
        return new Stereo32BitInteger(s1.Left + s2.Left, s1.Right + s2.Right);
    }

    public static unsafe void GetChannelData(Stereo32BitInteger s, int channel, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        if (channel is 0 or 1)
        {
            var span = new Span<byte>(channel == 0 ? &s.Left : &s.Right, sizeof(int));
            span.CopyTo(destination);
            bytesWritten = span.Length;
        }
    }

    public static int GetNumChannels()
    {
        return 2;
    }
}
