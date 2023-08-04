using Beutl.Media.Decoding;

namespace Beutl.Extensibility;

public abstract class DecodingExtension : Extension
{
    public abstract IDecoderInfo GetDecoderInfo();

    public override void Load()
    {
        DecoderRegistry.Register(GetDecoderInfo());
    }
}
