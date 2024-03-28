using Beutl.Extensibility;
using Beutl.Media.Decoding;
using MonoMac.AppKit;

namespace Beutl.Extensions.AVFoundation.Decoding;

[Export]
public class AVFDecodingExtension : DecodingExtension
{
    public override string Name => "AVFoundation Decoding";

    public override string DisplayName => "AVFoundation Decoding";

    public override IDecoderInfo GetDecoderInfo()
    {
        return new AVFDecoderInfo(this);
    }

    public override void Load()
    {
        if (OperatingSystem.IsMacOS())
        {
            NSApplication.Init();
            DecoderRegistry.Register(GetDecoderInfo());
        }
    }
}
