using Beutl.Extensibility;
using Beutl.Media.Decoding;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Decoding;
#else
namespace Beutl.Extensions.FFmpeg.Decoding;
#endif

[Export]
public class FFmpegDecodingExtension : DecodingExtension
{
    public override string Name => "FFmpeg Decoder";

    public override string DisplayName => "FFmpeg Decoder";

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
