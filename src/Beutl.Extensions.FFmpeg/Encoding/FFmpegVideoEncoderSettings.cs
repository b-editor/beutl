using Beutl.Media.Encoding;

using FFmpeg.AutoGen;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

public sealed class FFmpegVideoEncoderSettings : VideoEncoderSettings
{
    public static readonly CoreProperty<AVPixelFormat> FormatProperty;
    public static readonly CoreProperty<VideoCodec> CodecProperty;
    public static readonly CoreProperty<string> PresetProperty;
    public static readonly CoreProperty<int> CrfProperty;
    public static readonly CoreProperty<string> ProfileProperty;
    public static readonly CoreProperty<string> ArgumentsProperty;

    static FFmpegVideoEncoderSettings()
    {
        FormatProperty = ConfigureProperty<AVPixelFormat, FFmpegVideoEncoderSettings>(nameof(Format))
            .DefaultValue(AVPixelFormat.AV_PIX_FMT_NONE)
            .Register();

        CodecProperty = ConfigureProperty<VideoCodec, FFmpegVideoEncoderSettings>(nameof(Codec))
            .DefaultValue(VideoCodec.Default)
            .Register();

        PresetProperty = ConfigureProperty<string, FFmpegVideoEncoderSettings>(nameof(Preset))
            .DefaultValue("medium")
            .Register();

        CrfProperty = ConfigureProperty<int, FFmpegVideoEncoderSettings>(nameof(Crf))
            .DefaultValue(22)
            .Register();

        ProfileProperty = ConfigureProperty<string, FFmpegVideoEncoderSettings>(nameof(Profile))
            .DefaultValue("high")
            .Register();

        ArgumentsProperty = ConfigureProperty<string, FFmpegVideoEncoderSettings>(nameof(Arguments))
            .DefaultValue("")
            .Register();
    }

    public AVPixelFormat Format
    {
        get => GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    public VideoCodec Codec
    {
        get => GetValue(CodecProperty);
        set => SetValue(CodecProperty, value);
    }

    public string Preset
    {
        get => GetValue(PresetProperty);
        set => SetValue(PresetProperty, value);
    }

    public int Crf
    {
        get => GetValue(CrfProperty);
        set => SetValue(CrfProperty, value);
    }

    public string Profile
    {
        get => GetValue(ProfileProperty);
        set => SetValue(ProfileProperty, value);
    }

    public string Arguments
    {
        get => GetValue(ArgumentsProperty);
        set => SetValue(ArgumentsProperty, value);
    }

    public enum VideoCodec
    {
        Explicit = int.MinValue,
        Default = AVCodecID.AV_CODEC_ID_NONE,
        H263 = AVCodecID.AV_CODEC_ID_H263,
        H263I = AVCodecID.AV_CODEC_ID_H263I,
        H263P = AVCodecID.AV_CODEC_ID_H263P,
        H264 = AVCodecID.AV_CODEC_ID_H264,
        H265 = AVCodecID.AV_CODEC_ID_HEVC,
        WMV = AVCodecID.AV_CODEC_ID_WMV3,
        MPEG = AVCodecID.AV_CODEC_ID_MPEG1VIDEO,
        MPEG2 = AVCodecID.AV_CODEC_ID_MPEG2VIDEO,
        MPEG4 = AVCodecID.AV_CODEC_ID_MPEG4,
        VP8 = AVCodecID.AV_CODEC_ID_VP8,
        VP9 = AVCodecID.AV_CODEC_ID_VP9,
        Theora = AVCodecID.AV_CODEC_ID_THEORA,
        Dirac = AVCodecID.AV_CODEC_ID_DIRAC,
        MJPEG = AVCodecID.AV_CODEC_ID_MJPEG,
        AV1 = AVCodecID.AV_CODEC_ID_AV1,
        DV = AVCodecID.AV_CODEC_ID_DVVIDEO,
        Cinepak = AVCodecID.AV_CODEC_ID_CINEPAK,
    }
}
