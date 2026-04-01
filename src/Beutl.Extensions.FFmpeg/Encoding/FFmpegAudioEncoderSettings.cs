using Beutl.Media.Encoding;
using Beutl.Serialization;

#if BEUTL_FFMPEG_WORKER
namespace Beutl.FFmpegWorker.Encoding;
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

    public string? OutputFile { get; set; }

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
        Default = -1,
        U8 = 0,
        S16 = 1,
        S32 = 2,
        Flt = 3,
        Dbl = 4,
        U8p = 5,
        S16p = 6,
        S32p = 7,
        Fltp = 8,
        Dblp = 9,
        S64 = 10,
        S64p = 11
    }
}

public class AudioCodecChoicesProvider : IChoicesProvider
{
    public static IReadOnlyList<object> GetChoices()
    {
#if FFMPEG_OUT_OF_PROCESS
        return FFmpegWorkerCodecCache.GetAudioCodecs();
#else
        return FFmpegSharp.MediaCodec.GetCodecs()
            .Where(i => i.IsEncoder && i.Type == global::FFmpeg.AutoGen.Abstractions.AVMediaType.AVMEDIA_TYPE_AUDIO)
            .Select(i => (object)new CodecRecord(i.Name, i.LongName))
            .Prepend(CodecRecord.Default)
            .ToArray();
#endif
    }
}
