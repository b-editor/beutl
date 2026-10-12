using System.ComponentModel.DataAnnotations;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg.Properties;
using Beutl.Serialization;

namespace Beutl.Extensions.FFmpeg.Encoding;

// Beutl 1.x built this extension into the app as Beutl.Embedding.FFmpeg (FFmpegBuildIn, on by default up to
// v1.1.0), so the output profiles and presets it saved select this encoder by that name.
[Export]
[Display(Name = nameof(Strings.FFmpegEncoder), ResourceType = typeof(Strings))]
[FormerPersistedName("[Beutl.Embedding.FFmpeg].Encoding:FFmpegControlledEncodingExtension")]
public class FFmpegControlledEncodingExtension : ControllableEncodingExtension
{
    public override FFmpegEncodingSettings Settings { get; } = new();

    public override IEnumerable<string> SupportExtensions() => FFmpegHeadlessEncodingExtension.SupportedExtensions;

    public override EncodingController CreateController(string file)
    {
        return new FFmpegEncodingControllerProxy(file, Settings);
    }


    public override void Load()
    {
        FFmpegInstallNotifier.HookLibraryState();
        base.Load();
    }
}
