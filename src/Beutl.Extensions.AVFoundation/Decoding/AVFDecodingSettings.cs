using System.ComponentModel.DataAnnotations;
using Beutl.Extensibility;

namespace Beutl.Extensions.AVFoundation.Decoding;

public sealed class AVFDecodingSettings : ExtensionSettings
{
    public AVFDecodingSettings()
    {
        ScanProperties<AVFDecodingSettings>();
        ThresholdFrameCount.ValueChanged += (_, _) => OnChanged();
        ThresholdSampleCount.ValueChanged += (_, _) => OnChanged();
        MaxVideoBufferSize.ValueChanged += (_, _) => OnChanged();
        MaxAudioBufferSize.ValueChanged += (_, _) => OnChanged();
    }

    [Range(1, int.MaxValue)]
    public IProperty<int> ThresholdFrameCount { get; } = Property.CreateValue(30);

    [Range(1, int.MaxValue)]
    public IProperty<int> ThresholdSampleCount { get; } = Property.CreateValue(30000);

    [Range(1, int.MaxValue)]
    public IProperty<int> MaxVideoBufferSize { get; } = Property.CreateValue(4);

    [Range(1, int.MaxValue)]
    public IProperty<int> MaxAudioBufferSize { get; } = Property.CreateValue(20);
}
