using Beutl.Extensibility;
using Beutl.Media.Encoding;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

[Export]
public sealed class FFmpegEncodingExtension : EncodingExtension
{
    public override string Name => "FFmpeg Encoder";

    public override string DisplayName => "FFmpeg Encoder";

    public override FFmpegEncodingSettings Settings { get; } = new FFmpegEncodingSettings();

    public override IEncoderInfo GetEncoderInfo()
    {
        return new FFmpegEncoderInfo(Settings);
    }

    public override void Load()
    {
        FFmpegLoader.Initialize();
        base.Load();
    }
}
