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
    public static readonly CoreProperty<string> ArgumentsProperty;

    static FFmpegAudioEncoderSettings()
    {
        CodecProperty = ConfigureProperty<AudioCodec, FFmpegAudioEncoderSettings>(nameof(Codec))
            .DefaultValue(AudioCodec.Default)
            .Register();

        ArgumentsProperty = ConfigureProperty<string, FFmpegAudioEncoderSettings>(nameof(Arguments))
            .DefaultValue("")
            .Register();
    }

    public AudioCodec Codec
    {
        get => GetValue(CodecProperty);
        set => SetValue(CodecProperty, value);
    }

    public string Arguments
    {
        get => GetValue(ArgumentsProperty);
        set => SetValue(ArgumentsProperty, value);
    }

    public enum AudioCodec
    {
        Explicit = int.MinValue,
        Default = AVCodecID.AV_CODEC_ID_NONE,
        AAC = AVCodecID.AV_CODEC_ID_AAC,
        AC3 = AVCodecID.AV_CODEC_ID_AC3,
        MP3 = AVCodecID.AV_CODEC_ID_MP3,
        WMA = AVCodecID.AV_CODEC_ID_WMAV2,
        Vorbis = AVCodecID.AV_CODEC_ID_VORBIS,
    }
}
