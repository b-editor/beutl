using System.ComponentModel.DataAnnotations;
using Beutl.Extensibility;

namespace Beutl.Extensions.AVFoundation.Decoding;

public sealed class AVFDecodingSettings : ExtensionSettings
{
    public static readonly CoreProperty<int> ThresholdFrameCountProperty;
    public static readonly CoreProperty<int> ThresholdSampleCountProperty;
    public static readonly CoreProperty<int> MaxVideoBufferSizeProperty;
    public static readonly CoreProperty<int> MaxAudioBufferSizeProperty;

    static AVFDecodingSettings()
    {
        ThresholdFrameCountProperty = ConfigureProperty<int, AVFDecodingSettings>(nameof(ThresholdFrameCount))
            .DefaultValue(30)
            .Register();

        ThresholdSampleCountProperty = ConfigureProperty<int, AVFDecodingSettings>(nameof(ThresholdSampleCount))
            .DefaultValue(30000)
            .Register();

        MaxVideoBufferSizeProperty = ConfigureProperty<int, AVFDecodingSettings>(nameof(MaxVideoBufferSize))
            .DefaultValue(4)
            .Register();

        MaxAudioBufferSizeProperty = ConfigureProperty<int, AVFDecodingSettings>(nameof(MaxAudioBufferSize))
            .DefaultValue(20)
            .Register();

        AffectsConfig<AVFDecodingSettings>(
            ThresholdFrameCountProperty,
            ThresholdSampleCountProperty,
            MaxVideoBufferSizeProperty,
            MaxAudioBufferSizeProperty);
    }

    [Range(1, int.MaxValue)]
    public int ThresholdFrameCount
    {
        get => GetValue(ThresholdFrameCountProperty);
        set => SetValue(ThresholdFrameCountProperty, value);
    }

    [Range(1, int.MaxValue)]
    public int ThresholdSampleCount
    {
        get => GetValue(ThresholdSampleCountProperty);
        set => SetValue(ThresholdSampleCountProperty, value);
    }

    [Range(1, int.MaxValue)]
    public int MaxVideoBufferSize
    {
        get => GetValue(MaxVideoBufferSizeProperty);
        set => SetValue(MaxVideoBufferSizeProperty, value);
    }

    [Range(1, int.MaxValue)]
    public int MaxAudioBufferSize
    {
        get => GetValue(MaxAudioBufferSizeProperty);
        set => SetValue(MaxAudioBufferSizeProperty, value);
    }
}
