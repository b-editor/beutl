using System.ComponentModel.DataAnnotations;
using System.Runtime.Versioning;
using Beutl.Extensibility;
using Beutl.Extensions.MediaFoundation.Properties;
using Beutl.Media.Decoding;

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

    // The extension itself loads on any OS, but only registers this decoder on Windows.
    [SupportedOSPlatform("windows")]
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
