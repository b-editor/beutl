using Beutl.Extensibility;
using Beutl.Media.Encoding;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Encoding;
#else
namespace Beutl.Extensions.MediaFoundation.Encoding;
#endif

[Export]
public class MFEncodingExtension : EncodingExtension
{
    public override string Name => "Media Foundation Encoder";

    public override string DisplayName => "Media Foundation Encoder";

    public override IEncoderInfo GetEncoderInfo() => new MFEncoderInfo();

    public override void Load()
    {
        if (OperatingSystem.IsWindows())
        {
            EncoderRegistry.Register(GetEncoderInfo());
        }
    }
}
