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
    public AdditionalOption()
    {
        ScanProperties<AdditionalOption>();
        ValueProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(Value)));
    }

    public AdditionalOption(string name, string value)
        : this()
    {
        Name = name;
        Value = value;
    }

    public IProperty<string> ValueProperty { get; } = new ValueOnlyProperty<string>("");

    public string Value
    {
        get => ValueProperty.Value;
        set => ValueProperty.Value = value;
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
    public FFmpegVideoEncoderSettings()
    {
        ScanProperties<FFmpegVideoEncoderSettings>();
        Options =
        [
            new("preset", "medium"),
            new("crf", "22"),
            new("profile", "high"),
            new("level", "4.0")
        ];
    }

    public IProperty<int> FormatProperty { get; } = new ValueOnlyProperty<int>(FFPixelFormat.None);

    // Serialize and Deserialize write and read the codec by its name.
    [NotAutoSerialized]
    [ChoicesProvider(typeof(VideoCodecChoicesProvider))]
    public IProperty<CodecRecord> CodecProperty { get; } = new ValueOnlyProperty<CodecRecord>(CodecRecord.Default);

    public IProperty<FFColorPrimaries> ColorPrimariesProperty { get; } = new ValueOnlyProperty<FFColorPrimaries>(FFColorPrimaries.UNSPECIFIED);

    public IProperty<FFColorTransfer> ColorTrcProperty { get; } = new ValueOnlyProperty<FFColorTransfer>(FFColorTransfer.UNSPECIFIED);

    public IProperty<FFColorSpace> ColorSpaceProperty { get; } = new ValueOnlyProperty<FFColorSpace>(FFColorSpace.UNSPECIFIED);

    public IProperty<FFColorRange> ColorRangeProperty { get; } = new ValueOnlyProperty<FFColorRange>(FFColorRange.UNSPECIFIED);

    public IProperty<CoreList<AdditionalOption>> OptionsProperty { get; } = new ValueOnlyProperty<CoreList<AdditionalOption>>(null!);

    public string? OutputFile { get; set; }

    public int Format
    {
        get => FormatProperty.Value;
        set => FormatProperty.Value = value;
    }

    public CodecRecord Codec
    {
        get => CodecProperty.Value;
        set => CodecProperty.Value = value;
    }

    public FFColorPrimaries ColorPrimaries
    {
        get => ColorPrimariesProperty.Value;
        set => ColorPrimariesProperty.Value = value;
    }

    public FFColorTransfer ColorTrc
    {
        get => ColorTrcProperty.Value;
        set => ColorTrcProperty.Value = value;
    }

    public FFColorSpace ColorSpace
    {
        get => ColorSpaceProperty.Value;
        set => ColorSpaceProperty.Value = value;
    }

    public FFColorRange ColorRange
    {
        get => ColorRangeProperty.Value;
        set => ColorRangeProperty.Value = value;
    }

    public CoreList<AdditionalOption> Options
    {
        get => OptionsProperty.Value;
        set => OptionsProperty.Value = value;
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
#if !BEUTL_FFMPEG_WORKER
        return FFmpegWorkerCodecCache.GetVideoCodecs();
#else
        // The worker builds settings from IPC values and never deserializes them or shows choices.
        return [CodecRecord.Default];
#endif
    }
}
