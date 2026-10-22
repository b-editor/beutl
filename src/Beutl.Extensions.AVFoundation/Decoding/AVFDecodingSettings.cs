using System.ComponentModel.DataAnnotations;
using Beutl.Extensibility;

namespace Beutl.Extensions.AVFoundation.Decoding;

public sealed class AVFDecodingSettings : ExtensionSettings
{
    public AVFDecodingSettings()
    {
        ScanProperties<AVFDecodingSettings>();
        ThresholdFrameCountProperty.ValueChanged += (_, _) => OnChanged();
        ThresholdSampleCountProperty.ValueChanged += (_, _) => OnChanged();
        MaxVideoBufferSizeProperty.ValueChanged += (_, _) => OnChanged();
        MaxAudioBufferSizeProperty.ValueChanged += (_, _) => OnChanged();
    }

    [Range(1, int.MaxValue)]
    public IProperty<int> ThresholdFrameCountProperty { get; } = new ValueOnlyProperty<int>(30);

    [Range(1, int.MaxValue)]
    public IProperty<int> ThresholdSampleCountProperty { get; } = new ValueOnlyProperty<int>(30000);

    [Range(1, int.MaxValue)]
    public IProperty<int> MaxVideoBufferSizeProperty { get; } = new ValueOnlyProperty<int>(4);

    [Range(1, int.MaxValue)]
    public IProperty<int> MaxAudioBufferSizeProperty { get; } = new ValueOnlyProperty<int>(20);

    public int ThresholdFrameCount
    {
        get => ThresholdFrameCountProperty.Value;
        set => ThresholdFrameCountProperty.Value = value;
    }

    public int ThresholdSampleCount
    {
        get => ThresholdSampleCountProperty.Value;
        set => ThresholdSampleCountProperty.Value = value;
    }

    public int MaxVideoBufferSize
    {
        get => MaxVideoBufferSizeProperty.Value;
        set => MaxVideoBufferSizeProperty.Value = value;
    }

    public int MaxAudioBufferSize
    {
        get => MaxAudioBufferSizeProperty.Value;
        set => MaxAudioBufferSizeProperty.Value = value;
    }
}
