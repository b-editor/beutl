using System.Runtime.InteropServices;

namespace BeUtl.Media.Audio.Pcm;

[StructLayout(LayoutKind.Sequential)]
public struct Monaural16BitInteger : IPcm<Monaural16BitInteger>
{
    public short Value;

    public Monaural16BitInteger(short value)
    {
        Value = value;
    }

    public Monaural16BitInteger Compose(Monaural16BitInteger s)
    {
        return new Monaural16BitInteger((short)(Value + s.Value));
    }

    public Monaural16BitInteger ConvertFrom(Stereo32BitFloat data)
    {
        // Todo
        return new Monaural16BitInteger((short)MathF.Round(data.Left * short.MaxValue, MidpointRounding.AwayFromZero));
    }

    public Stereo32BitFloat ConvertTo()
    {
        float v = (float)Value / short.MaxValue;
        return new Stereo32BitFloat(v, v);
    }
}
