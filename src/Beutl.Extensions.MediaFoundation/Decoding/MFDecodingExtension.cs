using Beutl.Extensibility;
using Beutl.Extensions.MediaFoundation.Properties;
using Beutl.Media.Decoding;
using System.ComponentModel.DataAnnotations;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

[Export]
[Display(Name = nameof(Strings.DecodingName), ResourceType = typeof(Strings))]
public sealed class MFDecodingExtension : DecodingExtension
{
    public override MFDecodingSettings Settings { get; } = new MFDecodingSettings();

    public override IDecoderInfo GetDecoderInfo()
    {
        return new MFDecoderInfo(this);
    }

    public override void Load()
    {
        if (OperatingSystem.IsWindows())
        {
            DecoderRegistry.Register(GetDecoderInfo());
        }
    }
}
