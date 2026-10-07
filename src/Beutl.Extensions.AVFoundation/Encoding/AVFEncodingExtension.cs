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
    public override IEnumerable<string> SupportExtensions() => AVFFileExtensions.Video();

    public override EncodingController CreateController(string file)
    {
        return new AVFEncodingController(file);
    }
}
