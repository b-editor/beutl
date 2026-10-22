using Beutl.Media.Encoding;
using Beutl.Serialization;

#if BEUTL_FFMPEG_WORKER
namespace Beutl.FFmpegWorker.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

public sealed class FFmpegAudioEncoderSettings : AudioEncoderSettings
{
    public FFmpegAudioEncoderSettings()
    {
        ScanProperties<FFmpegAudioEncoderSettings>();
    }

    // Serialize and Deserialize write and read the codec by its name.
    [ChoicesProvider(typeof(AudioCodecChoicesProvider))]
    [NotAutoSerialized]
    public IProperty<CodecRecord> CodecProperty { get; } = new ValueOnlyProperty<CodecRecord>(CodecRecord.Default);

    public IProperty<AudioFormat> FormatProperty { get; } = new ValueOnlyProperty<AudioFormat>(AudioFormat.Fltp);

    public string? OutputFile { get; set; }

    public CodecRecord Codec
    {
        get => CodecProperty.Value;
        set => CodecProperty.Value = value;
    }

    public AudioFormat Format
    {
        get => FormatProperty.Value;
        set => FormatProperty.Value = value;
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
#if !BEUTL_FFMPEG_WORKER
        return FFmpegWorkerCodecCache.GetAudioCodecs();
#else
        // The worker builds settings from IPC values and never deserializes them or shows choices.
        return [CodecRecord.Default];
#endif
    }
}
