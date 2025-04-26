using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Beutl.Extensibility;
using Beutl.Media.Decoding;
using MonoMac.AppKit;

namespace Beutl.Extensions.AVFoundation.Decoding;

[Export]
[Display(Name = "AVFoundation Decoding")]
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
