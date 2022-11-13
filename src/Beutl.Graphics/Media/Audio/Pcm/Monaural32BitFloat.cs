using System.Runtime.InteropServices;

namespace Beutl.Media.Audio.Pcm;

[StructLayout(LayoutKind.Sequential)]
public struct Monaural32BitFloat : IPcm<Monaural32BitFloat>
{
    public float Value;

    public Monaural32BitFloat(float value)
    {
        Value = value;
    }

    public Monaural32BitFloat Compose(Monaural32BitFloat s)
    {
        return new Monaural32BitFloat(Value + s.Value);
    }

    public Monaural32BitFloat ConvertFrom(Stereo32BitFloat data)
    {
        // Todo
        return new Monaural32BitFloat(data.Left);
    }

    public Stereo32BitFloat ConvertTo()
    {
        return new Stereo32BitFloat(Value, Value);
    }
}
