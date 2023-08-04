using Beutl.Media.Encoding;

namespace Beutl.Extensibility;

public abstract class EncodingExtension : Extension
{
    public abstract IEncoderInfo GetEncoderInfo();

    public override void Load()
    {
        EncoderRegistry.Register(GetEncoderInfo());
    }
}
