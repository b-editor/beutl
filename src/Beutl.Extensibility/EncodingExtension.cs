using Beutl.Media.Encoding;

namespace Beutl.Extensibility;

[Obsolete("Use EncodingController instead.")]
public abstract class EncodingExtension : Extension
{
    public abstract IEncoderInfo GetEncoderInfo();

    public override void Load()
    {
        EncoderRegistry.Register(GetEncoderInfo());
    }
}
