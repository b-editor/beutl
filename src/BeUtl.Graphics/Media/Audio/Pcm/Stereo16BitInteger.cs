using System.Runtime.InteropServices;

namespace BeUtl.Media.Audio.Pcm;

[StructLayout(LayoutKind.Sequential)]
public struct Stereo16BitInteger : IPcm<Stereo16BitInteger>
{
    public short Left;

    public short Right;

    public Stereo16BitInteger(short left, short right)
    {
        Left = left;
        Right = right;
    }

    public Stereo16BitInteger Compose(Stereo16BitInteger s)
    {
        return new Stereo16BitInteger((short)(Left + s.Left), (short)(Right + s.Right));
    }

    public Stereo16BitInteger ConvertFrom(Stereo32BitFloat data)
    {
        return new Stereo16BitInteger(
            left: (short)MathF.Round(data.Left * short.MaxValue, MidpointRounding.AwayFromZero),
            right: (short)MathF.Round(data.Right * short.MaxValue, MidpointRounding.AwayFromZero));
    }

    public Stereo32BitFloat ConvertTo()
    {
        return new Stereo32BitFloat((float)Left / short.MaxValue, (float)Right / short.MaxValue);
    }
}
