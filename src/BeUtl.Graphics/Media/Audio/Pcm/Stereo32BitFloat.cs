using System.Runtime.InteropServices;

namespace BeUtl.Media.Audio.Pcm;

[StructLayout(LayoutKind.Sequential)]
public struct Stereo32BitFloat : IPcm<Stereo32BitFloat>
{
    public float Left;

    public float Right;

    public Stereo32BitFloat(float left, float right)
    {
        Left = left;
        Right = right;
    }

    public Stereo32BitFloat Compose(Stereo32BitFloat s)
    {
        return new Stereo32BitFloat(Left + s.Left, Right + s.Right);
    }

    public Stereo32BitFloat ConvertFrom(Stereo32BitFloat data)
    {
        return this;
    }

    public Stereo32BitFloat ConvertTo()
    {
        return this;
    }
}
