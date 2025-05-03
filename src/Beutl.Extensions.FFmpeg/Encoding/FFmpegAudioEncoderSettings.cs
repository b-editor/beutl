using Beutl.Media.Encoding;
using Beutl.Serialization;
using FFmpeg.AutoGen;
using FFmpegSharp;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

public sealed class FFmpegAudioEncoderSettings : AudioEncoderSettings
{
    public static readonly CoreProperty<CodecRecord> CodecProperty;
    public static readonly CoreProperty<AudioFormat> FormatProperty;

    static FFmpegAudioEncoderSettings()
    {
        CodecProperty = ConfigureProperty<CodecRecord, FFmpegAudioEncoderSettings>(nameof(Codec))
            .DefaultValue(CodecRecord.Default)
            .Register();

        FormatProperty = ConfigureProperty<AudioFormat, FFmpegAudioEncoderSettings>(nameof(Format))
            .DefaultValue(AudioFormat.Fltp)
            .Register();
    }

    [ChoicesProvider(typeof(AudioCodecChoicesProvider))]
    [NotAutoSerialized]
    public CodecRecord Codec
    {
        get => GetValue(CodecProperty);
        set => SetValue(CodecProperty, value);
    }

    public AudioFormat Format
    {
        get => GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        string? codecName = context.GetValue<string>(nameof(Codec));
        if (codecName != null)
        {
            Codec = AudioCodecChoicesProvider.GetChoices()
                .Cast<CodecRecord>()
                .FirstOrDefault(i => i.Name == codecName, CodecRecord.Default);
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Codec), Codec.Name);
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
}

public class AudioCodecChoicesProvider : IChoicesProvider
{
    public static IReadOnlyList<object> GetChoices()
    {
        return MediaCodec.GetCodecs()
            .Where(i => i.IsEncoder && i.Type == AVMediaType.AVMEDIA_TYPE_AUDIO)
            .Select(i => new CodecRecord(i.Name, i.LongName))
            .Prepend(CodecRecord.Default)
            .ToArray();
    }
}
