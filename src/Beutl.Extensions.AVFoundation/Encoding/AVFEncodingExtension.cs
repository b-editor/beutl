using System.ComponentModel.DataAnnotations;
using System.Runtime.Versioning;
using Beutl.Extensibility;
using Beutl.Language;

namespace Beutl.Extensions.AVFoundation.Encoding;

[Export]
[SupportedOSPlatform("macos")]
[Display(Name = nameof(Strings.AVFoundationEncoder), ResourceType = typeof(Strings))]
public class AVFEncodingExtension : ControllableEncodingExtension
{
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
