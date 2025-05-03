using System.Runtime.Versioning;
using Beutl.Extensibility;
using MonoMac.AppKit;
using System.ComponentModel.DataAnnotations;

namespace Beutl.Extensions.AVFoundation.Encoding;

[Export]
[SupportedOSPlatform("macos")]
[Display(Name = "AVFoundation Encoder")]
public class AVFEncodingExtension : ControllableEncodingExtension
{
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
        }
    }

    public override IEnumerable<string> SupportExtensions()
    {
        yield return ".mp4";
        yield return ".mov";
        yield return ".m4v";
        yield return ".avi";
        yield return ".wmv";
        yield return ".sami";
        yield return ".smi";
        yield return ".adts";
        yield return ".asf";
        yield return ".3gp";
        yield return ".3gp2";
        yield return ".3gpp";
    }

    public override EncodingController CreateController(string file)
    {
        return new AVFEncodingController(file);
    }
}
