using System.ComponentModel.DataAnnotations;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Media.Decoding;
using MonoMac.AppKit;

namespace Beutl.Extensions.AVFoundation.Decoding;

[Export]
[Display(Name = nameof(Strings.AVFoundationDecoder), ResourceType = typeof(Strings))]
public class AVFDecodingExtension : DecodingExtension
{
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
