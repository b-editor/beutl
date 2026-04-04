using System.ComponentModel;
using Beutl.Collections;
using Beutl.FFmpegIpc;
using Beutl.Media.Encoding;
using Beutl.Serialization;

#if BEUTL_FFMPEG_WORKER
namespace Beutl.FFmpegWorker.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

public sealed class AdditionalOption : CoreObject
{
    public static readonly CoreProperty<string> ValueProperty;

    static AdditionalOption()
    {
        ValueProperty = ConfigureProperty<string, AdditionalOption>(nameof(Value))
            .DefaultValue("")
            .Register();

        NameProperty.OverrideMetadata<AdditionalOption>(
            new CorePropertyMetadata<string>(attributes: [new BrowsableAttribute(true)]));
    }

    public AdditionalOption()
    {
    }

    public AdditionalOption(string name, string value)
    {
        Name = name;
        Value = value;
    }

    public string Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.Contains("Key"))
        {
            Name = context.GetValue<string>("Key") ?? Name;
        }
    }
}

public sealed class FFmpegVideoEncoderSettings : VideoEncoderSettings
{
    public static readonly CoreProperty<int> FormatProperty;
    public static readonly CoreProperty<CodecRecord> CodecProperty;
    public static readonly CoreProperty<FFColorPrimaries> ColorPrimariesProperty;
    public static readonly CoreProperty<FFColorTransfer> ColorTrcProperty;
    public static readonly CoreProperty<FFColorSpace> ColorSpaceProperty;
    public static readonly CoreProperty<FFColorRange> ColorRangeProperty;
    public static readonly CoreProperty<CoreList<AdditionalOption>> OptionsProperty;

    static FFmpegVideoEncoderSettings()
    {
        FormatProperty = ConfigureProperty<int, FFmpegVideoEncoderSettings>(nameof(Format))
            .DefaultValue(FFPixelFormat.None)
            .Register();

        CodecProperty = ConfigureProperty<CodecRecord, FFmpegVideoEncoderSettings>(nameof(Codec))
            .DefaultValue(CodecRecord.Default)
            .Register();

        ColorPrimariesProperty = ConfigureProperty<FFColorPrimaries, FFmpegVideoEncoderSettings>(nameof(ColorPrimaries))
            .DefaultValue(FFColorPrimaries.UNSPECIFIED)
            .Register();

        ColorTrcProperty = ConfigureProperty<FFColorTransfer, FFmpegVideoEncoderSettings>(nameof(ColorTrc))
            .DefaultValue(FFColorTransfer.UNSPECIFIED)
            .Register();

        ColorSpaceProperty = ConfigureProperty<FFColorSpace, FFmpegVideoEncoderSettings>(nameof(ColorSpace))
            .DefaultValue(FFColorSpace.UNSPECIFIED)
            .Register();

        ColorRangeProperty = ConfigureProperty<FFColorRange, FFmpegVideoEncoderSettings>(nameof(ColorRange))
            .DefaultValue(FFColorRange.UNSPECIFIED)
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

    public string? OutputFile { get; set; }

    public int Format
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

    public FFColorPrimaries ColorPrimaries
    {
        get => GetValue(ColorPrimariesProperty);
        set => SetValue(ColorPrimariesProperty, value);
    }

    public FFColorTransfer ColorTrc
    {
        get => GetValue(ColorTrcProperty);
        set => SetValue(ColorTrcProperty, value);
    }

    public FFColorSpace ColorSpace
    {
        get => GetValue(ColorSpaceProperty);
        set => SetValue(ColorSpaceProperty, value);
    }

    public FFColorRange ColorRange
    {
        get => GetValue(ColorRangeProperty);
        set => SetValue(ColorRangeProperty, value);
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

        AdditionalOption? option = Options.FirstOrDefault(i => i.Name == newKey);
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
#if FFMPEG_OUT_OF_PROCESS
        return FFmpegWorkerCodecCache.GetVideoCodecs();
#else
        return FFmpegSharp.MediaCodec.GetCodecs()
            .Where(i => i.IsEncoder && i.Type == global::FFmpeg.AutoGen.Abstractions.AVMediaType.AVMEDIA_TYPE_VIDEO)
            .Select(i => (object)new CodecRecord(i.Name, i.LongName))
            .Prepend(CodecRecord.Default)
            .ToArray();
#endif
    }
}
