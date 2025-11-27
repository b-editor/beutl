using Beutl.Collections;
using Beutl.Media.Encoding;
using Beutl.Serialization;
using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

public sealed class AdditionalOption : CoreObject
{
    public static readonly CoreProperty<string> KeyProperty;
    public static readonly CoreProperty<string> ValueProperty;

    static AdditionalOption()
    {
        KeyProperty = ConfigureProperty<string, AdditionalOption>(nameof(Key))
            .DefaultValue("")
            .Register();

        ValueProperty = ConfigureProperty<string, AdditionalOption>(nameof(Value))
            .DefaultValue("")
            .Register();
    }

    public AdditionalOption()
    {
    }

    public AdditionalOption(string key, string value)
    {
        Key = key;
        Value = value;
    }

    public string Key
    {
        get => GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    public string Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }
}

public sealed class FFmpegVideoEncoderSettings : VideoEncoderSettings
{
    public static readonly CoreProperty<AVPixelFormat> FormatProperty;
    public static readonly CoreProperty<CodecRecord> CodecProperty;
    public static readonly CoreProperty<CoreList<AdditionalOption>> OptionsProperty;

    static FFmpegVideoEncoderSettings()
    {
        FormatProperty = ConfigureProperty<AVPixelFormat, FFmpegVideoEncoderSettings>(nameof(Format))
            .DefaultValue(AVPixelFormat.AV_PIX_FMT_NONE)
            .Register();

        CodecProperty = ConfigureProperty<CodecRecord, FFmpegVideoEncoderSettings>(nameof(Codec))
            .DefaultValue(CodecRecord.Default)
            .Register();

        OptionsProperty = ConfigureProperty<CoreList<AdditionalOption>, FFmpegVideoEncoderSettings>(nameof(Options))
            .Register();
    }

    public FFmpegVideoEncoderSettings()
    {
        Options =
        [
            new("preset", "medium"),
            new("crf", "22"),
            new("profile", "high"),
            new("level", "4.0")
        ];
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

    public CoreList<AdditionalOption> Options
    {
        get => GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
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

        SetOption(context, "Preset", "preset");
        SetOption(context, "Crf", "crf");
        SetOption(context, "Profile", "profile");
        SetOption(context, "Level", "level");
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Codec), Codec.Name);
    }

    private void SetOption(ICoreSerializationContext context, string key, string newKey)
    {
        if (!context.Contains(key)) return;

        string? value = context.GetValue<string>(key);
        if (string.IsNullOrEmpty(value)) return;
        bool unset = value.Equals("(unset)", StringComparison.OrdinalIgnoreCase);

        AdditionalOption? option = Options.FirstOrDefault(i => i.Key == newKey);
        if (option == null)
        {
            if (unset) return;
            option = new AdditionalOption(newKey, value);
            Options.Add(option);
        }
        else
        {
            if (unset)
            {
                Options.Remove(option);
                return;
            }

            option.Value = value;
        }
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
