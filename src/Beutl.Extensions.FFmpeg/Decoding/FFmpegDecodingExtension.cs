using System.ComponentModel.DataAnnotations;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg;
using Beutl.Extensions.FFmpeg.Properties;
using Beutl.Media.Decoding;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Decoding;
#else
namespace Beutl.Extensions.FFmpeg.Decoding;
#endif

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
        FFmpegLoader.Initialize();
        base.Load();
    }
}
