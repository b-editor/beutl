using System.ComponentModel.DataAnnotations;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg.Properties;
using Beutl.FFmpegIpc;
using Beutl.Media.Decoding;

namespace Beutl.Extensions.FFmpeg.Decoding;

[Export]
[Display(Name = nameof(Strings.FFmpegDecoder), ResourceType = typeof(Strings))]
public class FFmpegDecodingExtension : DecodingExtension
{
    public override FFmpegDecodingSettings Settings { get; } = new FFmpegDecodingSettings();

    public override IDecoderInfo GetDecoderInfo()
    {
        return new FFmpegDecoderInfo(Settings);
    }

    public override void Load()
    {
        try
        {
            FFmpegWorkerProcess.DecodingInstance.EnsureStarted();
        }
        catch (FFmpegLibrariesNotFoundException)
        {
            FFmpegInstallNotifier.NotifyMissing();
        }
        base.Load();
    }
}
