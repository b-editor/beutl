using Beutl.Extensibility;
using Beutl.Media.Decoding;
using MonoMac.AppKit;

namespace Beutl.Extensions.AVFoundation.Decoding;

[Export]
public class AVFDecodingExtension : DecodingExtension
{
    public override string Name => "AVFoundation Decoding";

    public override string DisplayName => "AVFoundation Decoding";

    public override AVFDecodingSettings? Settings { get; } = new();

    public override IDecoderInfo GetDecoderInfo()
    {
        return new AVFDecoderInfo(this);
    }

    public override void Load()
    {
        if (OperatingSystem.IsMacOS())
        {
            try
            {
                NSApplication.Init();
            }
            catch
            {
            }

            DecoderRegistry.Register(GetDecoderInfo());
        }
    }
}
