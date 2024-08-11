using Beutl.Media.Encoding;
using Beutl.Serialization;
using FFmpeg.AutoGen;
using FFmpegSharp;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

public sealed class FFmpegVideoEncoderSettings : VideoEncoderSettings
{
    public static readonly CoreProperty<AVPixelFormat> FormatProperty;
    public static readonly CoreProperty<CodecRecord> CodecProperty;
    public static readonly CoreProperty<string> PresetProperty;
    public static readonly CoreProperty<string> CrfProperty;
    public static readonly CoreProperty<string> ProfileProperty;
    public static readonly CoreProperty<string> LevelProperty;

    static FFmpegVideoEncoderSettings()
    {
        FormatProperty = ConfigureProperty<AVPixelFormat, FFmpegVideoEncoderSettings>(nameof(Format))
            .DefaultValue(AVPixelFormat.AV_PIX_FMT_NONE)
            .Register();

        CodecProperty = ConfigureProperty<CodecRecord, FFmpegVideoEncoderSettings>(nameof(Codec))
            .DefaultValue(CodecRecord.Default)
            .Register();

        PresetProperty = ConfigureProperty<string, FFmpegVideoEncoderSettings>(nameof(Preset))
            .DefaultValue("medium")
            .Register();

        CrfProperty = ConfigureProperty<string, FFmpegVideoEncoderSettings>(nameof(Crf))
            .DefaultValue("22")
            .Register();

        ProfileProperty = ConfigureProperty<string, FFmpegVideoEncoderSettings>(nameof(Profile))
            .DefaultValue("high")
            .Register();

        LevelProperty = ConfigureProperty<string, FFmpegVideoEncoderSettings>(nameof(Level))
            .DefaultValue("4.0")
            .Register();
    }

    public AVPixelFormat Format
    {
        get => GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    [NotAutoSerialized]
    [ChoicesProvider(typeof(VideoCodecChoicesProvider))]
    public CodecRecord Codec
    {
        get => GetValue(CodecProperty);
        set => SetValue(CodecProperty, value);
    }

    public string Preset
    {
        get => GetValue(PresetProperty);
        set => SetValue(PresetProperty, value);
    }

    public string Crf
    {
        get => GetValue(CrfProperty);
        set => SetValue(CrfProperty, value);
    }

    public string Profile
    {
        get => GetValue(ProfileProperty);
        set => SetValue(ProfileProperty, value);
    }

    public string Level
    {
        get => GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        string? codecName = context.GetValue<string>(nameof(Codec));
        if (codecName != null)
        {
            Codec = VideoCodecChoicesProvider.GetChoices()
                .Cast<CodecRecord>()
                .FirstOrDefault(i => i.Name == codecName, CodecRecord.Default);
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Codec), Codec.Name);
    }
}

public class VideoCodecChoicesProvider : IChoicesProvider
{
    public static IReadOnlyList<object> GetChoices()
    {
        return MediaCodec.GetCodecs()
            .Where(i => i.IsEncoder && i.Type == AVMediaType.AVMEDIA_TYPE_VIDEO)
            .Select(i => new CodecRecord(i.Name, i.LongName))
            .Prepend(CodecRecord.Default)
            .ToArray();
    }
}
