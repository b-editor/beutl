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
    }

    public AdditionalOption(string name, string value)
        : this()
    {
        Name.Value = name;
        Value.Value = value;
    }

    public IProperty<string> Value { get; } = Property.CreateValue("");

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.Contains("Key"))
        {
            Name.Value = context.GetValue<string>("Key") ?? Name.Value;
        }
    }
}

public sealed class FFmpegVideoEncoderSettings : VideoEncoderSettings
{
    public FFmpegVideoEncoderSettings()
    {
        ScanProperties<FFmpegVideoEncoderSettings>();
        Options.Value =
        [
            new("preset", "medium"),
            new("crf", "22"),
            new("profile", "high"),
            new("level", "4.0")
        ];
    }

    public IProperty<int> Format { get; } = Property.CreateValue(FFPixelFormat.None);

    // Serialize and Deserialize write and read the codec by its name.
    [NotAutoSerialized]
    [ChoicesProvider(typeof(VideoCodecChoicesProvider))]
    public IProperty<CodecRecord> Codec { get; } = Property.CreateValue(CodecRecord.Default);

    public IProperty<FFColorPrimaries> ColorPrimaries { get; } = Property.CreateValue(FFColorPrimaries.UNSPECIFIED);

    public IProperty<FFColorTransfer> ColorTrc { get; } = Property.CreateValue(FFColorTransfer.UNSPECIFIED);

    public IProperty<FFColorSpace> ColorSpace { get; } = Property.CreateValue(FFColorSpace.UNSPECIFIED);

    public IProperty<FFColorRange> ColorRange { get; } = Property.CreateValue(FFColorRange.UNSPECIFIED);

    public IProperty<CoreList<AdditionalOption>> Options { get; } = Property.CreateValue<CoreList<AdditionalOption>>(null!);

    public string? OutputFile { get; set; }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        string? codecName = context.GetValue<string>(nameof(Codec));
        if (codecName != null)
        {
            Codec.Value = VideoCodecChoicesProvider.GetChoices()
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
        context.SetValue(nameof(Codec), Codec.Value.Name);
    }

    private void SetOption(ICoreSerializationContext context, string key, string newKey)
    {
        if (!context.Contains(key)) return;

        string? value = context.GetValue<string>(key);
        if (string.IsNullOrEmpty(value)) return;
        bool unset = value.Equals("(unset)", StringComparison.OrdinalIgnoreCase);

        AdditionalOption? option = Options.Value.FirstOrDefault(i => i.Name.Value == newKey);
        if (option == null)
        {
            if (unset) return;
            option = new AdditionalOption(newKey, value);
            Options.Value.Add(option);
        }
        else
        {
            if (unset)
            {
                Options.Value.Remove(option);
                return;
            }

            option.Value.Value = value;
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
