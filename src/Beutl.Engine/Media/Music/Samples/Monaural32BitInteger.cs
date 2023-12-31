using System.Runtime.InteropServices;

namespace Beutl.Media.Music.Samples;

[StructLayout(LayoutKind.Sequential)]
public struct Monaural32BitInteger(int value) : ISample<Monaural32BitInteger>
{
    public int Value = value;

    public static Monaural32BitInteger ConvertFrom(Sample src)
    {
        return new Monaural32BitInteger((int)MathF.Round(src.Left * int.MaxValue, MidpointRounding.AwayFromZero));
    }

    public static Sample ConvertTo(Monaural32BitInteger src)
    {
        float v = (float)src.Value / int.MaxValue;
        return new Sample(v, v);
    }

    public static Monaural32BitInteger Amplifier(Monaural32BitInteger s, Sample level)
    {
        return new Monaural32BitInteger((int)MathF.Round(s.Value * level.Left, MidpointRounding.AwayFromZero));
    }

    public static Monaural32BitInteger Compound(Monaural32BitInteger s1, Monaural32BitInteger s2)
    {
        return new Monaural32BitInteger(s1.Value + s2.Value);
    }

    public static unsafe void GetChannelData(Monaural32BitInteger s, int channel, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        if (channel == 0)
        {
            var span = new Span<byte>(&s, sizeof(Monaural32BitInteger));
            span.CopyTo(destination);
            bytesWritten = span.Length;
        }
    }

    public static int GetNumChannels()
    {
        return 1;
    }
}
