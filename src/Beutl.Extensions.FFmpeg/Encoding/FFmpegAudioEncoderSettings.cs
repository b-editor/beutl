using Beutl.Media.Encoding;

using FFmpeg.AutoGen;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

public sealed class FFmpegAudioEncoderSettings : AudioEncoderSettings
{
    public static readonly CoreProperty<AudioCodec> CodecProperty;
    public static readonly CoreProperty<AudioFormat> FormatProperty;

    static FFmpegAudioEncoderSettings()
    {
        CodecProperty = ConfigureProperty<AudioCodec, FFmpegAudioEncoderSettings>(nameof(Codec))
            .DefaultValue(AudioCodec.Default)
            .Register();

        FormatProperty = ConfigureProperty<AudioFormat, FFmpegAudioEncoderSettings>(nameof(Format))
            .DefaultValue(AudioFormat.Fltp)
            .Register();
    }

    public AudioCodec Codec
    {
        get => GetValue(CodecProperty);
        set => SetValue(CodecProperty, value);
    }

    public AudioFormat Format
    {
        get => GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    public enum AudioFormat
    {
        Default = AVSampleFormat.AV_SAMPLE_FMT_NONE,
        S16 = AVSampleFormat.AV_SAMPLE_FMT_S16,
        S32 = AVSampleFormat.AV_SAMPLE_FMT_S32,
        S64 = AVSampleFormat.AV_SAMPLE_FMT_S64,
        Flt = AVSampleFormat.AV_SAMPLE_FMT_FLT,
        Dbl = AVSampleFormat.AV_SAMPLE_FMT_DBL,
        S16p = AVSampleFormat.AV_SAMPLE_FMT_S16P,
        S32p = AVSampleFormat.AV_SAMPLE_FMT_S32P,
        S64p = AVSampleFormat.AV_SAMPLE_FMT_S64P,
        Fltp = AVSampleFormat.AV_SAMPLE_FMT_FLTP,
        Dblp = AVSampleFormat.AV_SAMPLE_FMT_DBLP,
    }

    public enum AudioCodec
    {
        Default = AVCodecID.AV_CODEC_ID_NONE,
        AAC = AVCodecID.AV_CODEC_ID_AAC,
        AC3 = AVCodecID.AV_CODEC_ID_AC3,
        MP3 = AVCodecID.AV_CODEC_ID_MP3,
        WMA = AVCodecID.AV_CODEC_ID_WMAV2,
        Vorbis = AVCodecID.AV_CODEC_ID_VORBIS,
    }
}
