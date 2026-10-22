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
    public MFDecodingSettings()
    {
        ScanProperties<MFDecodingSettings>();
        ThresholdFrameCountProperty.ValueChanged += (_, _) => OnChanged();
        MaxVideoBufferSizeProperty.ValueChanged += (_, _) => OnChanged();
    }

    [Range(1, int.MaxValue)]
    [Display(
        Name = nameof(Strings.SeekThresholdInVideoStream),
        Description = nameof(Strings.SeekThresholdInVideoStream_Description),
        ResourceType = typeof(Strings))]
    public IProperty<int> ThresholdFrameCountProperty { get; } = new ValueOnlyProperty<int>(30);

    [Range(1, int.MaxValue)]
    [Display(
        Name = nameof(Strings.MaxVideoBufferSize),
        GroupName = nameof(Strings.Cache),
        ResourceType = typeof(Strings))]
    public IProperty<int> MaxVideoBufferSizeProperty { get; } = new ValueOnlyProperty<int>(4);

    public int ThresholdFrameCount
    {
        get => ThresholdFrameCountProperty.Value;
        set => ThresholdFrameCountProperty.Value = value;
    }

    public int MaxVideoBufferSize
    {
        get => MaxVideoBufferSizeProperty.Value;
        set => MaxVideoBufferSizeProperty.Value = value;
    }
}
