using System.Runtime.InteropServices;

namespace BeUtl.Media.Audio.Pcm;

[StructLayout(LayoutKind.Sequential)]
public struct Stereo32BitInteger : IPcm<Stereo32BitInteger>
{
    public int Left;

    public int Right;

    public Stereo32BitInteger(int left, int right)
    {
        Left = left;
        Right = right;
    }

    public Stereo32BitInteger Compose(Stereo32BitInteger s)
    {
        return new Stereo32BitInteger(Left + s.Left, Right + s.Right);
    }

    public Stereo32BitInteger ConvertFrom(Stereo32BitFloat data)
    {
        return new Stereo32BitInteger(
            left: (int)MathF.Round(data.Left * int.MaxValue, MidpointRounding.AwayFromZero),
            right: (int)MathF.Round(data.Right * int.MaxValue, MidpointRounding.AwayFromZero));
    }

    public Stereo32BitFloat ConvertTo()
    {
        return new Stereo32BitFloat((float)Left / int.MaxValue, (float)Right / int.MaxValue);
    }
}
