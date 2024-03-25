using System.Text.Json.Nodes;

using Beutl.Media;
using Beutl.Media.Encoding;

using FFmpeg.AutoGen;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

public sealed class FFmpegEncoderInfo(FFmpegEncodingSettings settings) : IEncoderInfo
{
    public string Name => "FFmpeg Encoder";

    public MediaWriter? Create(string file, VideoEncoderSettings videoConfig, AudioEncoderSettings audioConfig)
    {
        return new FFmpegWriter(file, videoConfig, audioConfig, settings);
    }

    public AudioEncoderSettings DefaultAudioConfig()
    {
        return new()
        {
            CodecOptions = new JsonObject()
            {
                ["Codec"] = ffmpeg.avcodec_get_name(AVCodecID.AV_CODEC_ID_NONE),
                ["Arguments"] = "",
            }
        };
    }

    public VideoEncoderSettings DefaultVideoConfig()
    {
        return new(new PixelSize(1920, 1080), new PixelSize(1920, 1080), new(30))
        {
            CodecOptions = new JsonObject()
            {
                ["Format"] = ffmpeg.av_get_pix_fmt_name(AVPixelFormat.AV_PIX_FMT_NONE),
                ["Codec"] = ffmpeg.avcodec_get_name(AVCodecID.AV_CODEC_ID_NONE),
                ["Preset"] = "medium",
                ["Crf"] = "22",
                ["Profile"] = "high",
                ["Arguments"] = "",
            }
        };
    }

    public IEnumerable<string> SupportExtensions()
    {
        yield return ".mp4";
        yield return ".wav";
        yield return ".mp3";
        yield return ".wmv";
        yield return ".avi";
        yield return ".webm";
        yield return ".3gp";
        yield return ".3g2";
        yield return ".flv";
        yield return ".mkv";
        yield return ".mov";
        yield return ".ogv";
    }
}
