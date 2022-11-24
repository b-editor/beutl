using System.ComponentModel;

using Beutl.Framework;
using Beutl.Media;
using Beutl.Media.Encoding;

using FFmpeg.AutoGen;

namespace Beutl.Extensions.FFmpeg.Encoding;

[Export]
public sealed class FFmpegEncodingExtension : EncodingExtension
{
    public override string Name => "FFmpeg Encoder";

    public override string DisplayName => "FFmpeg Encoder";

    public override IEncoderInfo GetEncoderInfo()
    {
        return new FFmpegEncoderInfo();
    }
}

public sealed class FFmpegEncoderInfo : IEncoderInfo
{
    public string Name => "FFmpeg Encoder";

    public MediaWriter? Create(string file, VideoEncoderSettings videoConfig, AudioEncoderSettings audioConfig)
    {
        return new FFmpegWriter(file, videoConfig, audioConfig);
    }

    public AudioEncoderSettings DefaultAudioConfig()
    {
        return new()
        {
            CodecOptions =
            {
                { "Format", AVSampleFormat.AV_SAMPLE_FMT_FLTP },
                { "Codec", AVCodecID.AV_CODEC_ID_NONE },
            }
        };
    }

    public VideoEncoderSettings DefaultVideoConfig()
    {
        return new(new PixelSize(1920, 1080), new(30))
        {
            CodecOptions =
            {
                { "Format", AVPixelFormat.AV_PIX_FMT_YUV420P },
                { "Codec", AVCodecID.AV_CODEC_ID_NONE },
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
