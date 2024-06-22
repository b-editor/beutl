using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ObjectiveC;
using System.Runtime.Versioning;
using Beutl.Extensibility;
using Beutl.Media.Decoding;
using Beutl.Media.Encoding;
using MonoMac.AppKit;

namespace Beutl.Extensions.AVFoundation.Encoding;

[Export]
public class AVFEncodingExtension : EncodingExtension
{
    public override string Name => "AVFoundation Encoding";

    public override string DisplayName => "AVFoundation Encoding";

    [SupportedOSPlatform("macos")]
    public override IEncoderInfo GetEncoderInfo()
    {
        return new AVFEncoderInfo(this);
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

            EncoderRegistry.Register(GetEncoderInfo());
        }
    }
}
