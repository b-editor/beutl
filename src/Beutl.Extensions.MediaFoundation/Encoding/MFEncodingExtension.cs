using System.ComponentModel.DataAnnotations;
using System.Runtime.Versioning;
using Beutl.Extensibility;
using Beutl.Extensions.MediaFoundation.Properties;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Encoding;
#else
namespace Beutl.Extensions.MediaFoundation.Encoding;
#endif

[Export]
[SupportedOSPlatform("windows")]
[Display(Name = nameof(Strings.EncodingName), ResourceType = typeof(Strings))]
public class MFEncodingExtension : ControllableEncodingExtension
{
    // Subset of Sink Writer container-type coverage. Anything outside this list
    // (e.g. .mkv, .ogg) can't be muxed by Media Foundation — users should fall
    // back to the FFmpeg encoder extension for those.
    public override IEnumerable<string> SupportExtensions()
    {
        yield return ".mp4";
        yield return ".mov";
        yield return ".m4v";
        yield return ".m4a";
        yield return ".wmv";
        yield return ".asf";
        yield return ".wav";
        yield return ".mp3";
        yield return ".aac";
        yield return ".adts";
        yield return ".3gp";
        yield return ".3gp2";
        yield return ".3gpp";
    }

    public override EncodingController CreateController(string file)
    {
        return new MFEncodingController(file);
    }
}
