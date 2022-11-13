using System.Runtime.InteropServices;

namespace Beutl.Media.Audio.Pcm;

[StructLayout(LayoutKind.Sequential)]
public struct Monaural32BitInteger : IPcm<Monaural32BitInteger>
{
    public int Value;

    public Monaural32BitInteger(int value)
    {
        Value = value;
    }

    public Monaural32BitInteger Compose(Monaural32BitInteger s)
    {
        return new Monaural32BitInteger(Value + s.Value);
    }

    public Monaural32BitInteger ConvertFrom(Stereo32BitFloat data)
    {
        // Todo
        return new Monaural32BitInteger((int)MathF.Round(data.Left * int.MaxValue, MidpointRounding.AwayFromZero));
    }

    public Stereo32BitFloat ConvertTo()
    {
        float v = (float)Value / int.MaxValue;
        return new Stereo32BitFloat(v, v);
    }
}
