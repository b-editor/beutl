using Beutl.Extensibility;
using Beutl.Media.Decoding;
using System.ComponentModel.DataAnnotations;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Decoding;
#else
namespace Beutl.Extensions.FFmpeg.Decoding;
#endif

[Export]
[Display(Name = "FFmpeg Decoder")]
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
