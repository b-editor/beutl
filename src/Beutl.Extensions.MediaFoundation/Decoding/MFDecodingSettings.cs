using System.ComponentModel.DataAnnotations;

using Beutl.Extensibility;
using Beutl.Extensions.MediaFoundation.Properties;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

public sealed class MFDecodingSettings : ExtensionSettings
{
    public static readonly CoreProperty<bool> UseDXVA2Property;
    public static readonly CoreProperty<int> ThresholdFrameCountProperty;
    public static readonly CoreProperty<int> ThresholdSampleCountProperty;
    public static readonly CoreProperty<int> MaxVideoBufferSizeProperty;
    public static readonly CoreProperty<int> MaxAudioBufferSizeProperty;

    static MFDecodingSettings()
    {
        UseDXVA2Property = ConfigureProperty<bool, MFDecodingSettings>(nameof(UseDXVA2))
            .DefaultValue(true)
            .Register();

        ThresholdFrameCountProperty = ConfigureProperty<int, MFDecodingSettings>(nameof(ThresholdFrameCount))
            .DefaultValue(30)
            .Register();

        ThresholdSampleCountProperty = ConfigureProperty<int, MFDecodingSettings>(nameof(ThresholdSampleCount))
            .DefaultValue(30000)
            .Register();

        MaxVideoBufferSizeProperty = ConfigureProperty<int, MFDecodingSettings>(nameof(MaxVideoBufferSize))
            .DefaultValue(4)
            .Register();

        MaxAudioBufferSizeProperty = ConfigureProperty<int, MFDecodingSettings>(nameof(MaxAudioBufferSize))
            .DefaultValue(20)
            .Register();

        AffectsConfig<MFDecodingSettings>(UseDXVA2Property);
    }

    [Display(Name = nameof(Strings.UseDXVA2), Description = nameof(Strings.UseDXVA2_Description), ResourceType = typeof(Strings))]
    public bool UseDXVA2
    {
        get => GetValue(UseDXVA2Property);
        set => SetValue(UseDXVA2Property, value);
    }

    [Range(1, int.MaxValue)]
    [Display(
        Name = nameof(Strings.SeekThresholdInVideoStream),
        Description = nameof(Strings.SeekThresholdInVideoStream_Description),
        ResourceType = typeof(Strings))]
    public int ThresholdFrameCount
    {
        get => GetValue(ThresholdFrameCountProperty);
        set => SetValue(ThresholdFrameCountProperty, value);
    }

    [Range(1, int.MaxValue)]
    [Display(
        Name = nameof(Strings.SeekThresholdInAudioStream),
        Description = nameof(Strings.SeekThresholdInAudioStream_Description),
        ResourceType = typeof(Strings))]
    public int ThresholdSampleCount
    {
        get => GetValue(ThresholdSampleCountProperty);
        set => SetValue(ThresholdSampleCountProperty, value);
    }

    [Range(1, int.MaxValue)]
    [Display(
        Name = nameof(Strings.MaxVideoBufferSize),
        GroupName = nameof(Strings.Cache),
        ResourceType = typeof(Strings))]
    public int MaxVideoBufferSize
    {
        get => GetValue(MaxVideoBufferSizeProperty);
        set => SetValue(MaxVideoBufferSizeProperty, value);
    }

    [Range(1, int.MaxValue)]
    [Display(
        Name = nameof(Strings.MaxAudioBufferSize),
        GroupName = nameof(Strings.Cache),
        ResourceType = typeof(Strings))]
    public int MaxAudioBufferSize
    {
        get => GetValue(MaxAudioBufferSizeProperty);
        set => SetValue(MaxAudioBufferSizeProperty, value);
    }
}
