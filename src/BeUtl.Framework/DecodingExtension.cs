using BeUtl.Media.Decoding;

namespace BeUtl.Framework;

public abstract class DecodingExtension : Extension
{
    public abstract IDecoderInfo GetDecoderInfo();

    public override void Load()
    {
        DecoderRegistry.Register(GetDecoderInfo());
    }
}
