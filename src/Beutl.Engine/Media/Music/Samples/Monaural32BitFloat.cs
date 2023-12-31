using System.Runtime.InteropServices;

namespace Beutl.Media.Music.Samples;

[StructLayout(LayoutKind.Sequential)]
public struct Monaural32BitFloat(float value) : ISample<Monaural32BitFloat>
{
    public float Value = value;

    public static Monaural32BitFloat Amplifier(Monaural32BitFloat s, Sample level)
    {
        return new Monaural32BitFloat(s.Value * level.Left);
    }

    public static Monaural32BitFloat Compound(Monaural32BitFloat s1, Monaural32BitFloat s2)
    {
        return new Monaural32BitFloat(s1.Value + s2.Value);
    }

    public static Monaural32BitFloat ConvertFrom(Sample src)
    {
        return new Monaural32BitFloat(src.Left);
    }

    public static Sample ConvertTo(Monaural32BitFloat src)
    {
        return new Sample(src.Value, src.Value);
    }

    public static unsafe void GetChannelData(Monaural32BitFloat s, int channel, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        if (channel == 0)
        {
            var span = new Span<byte>(&s, sizeof(Monaural32BitFloat));
            span.CopyTo(destination);
            bytesWritten = span.Length;
        }
    }

    public static int GetNumChannels()
    {
        return 1;
    }
}
