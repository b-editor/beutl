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
    public static readonly CoreProperty<int> ThresholdFrameCountProperty;
    public static readonly CoreProperty<int> MaxVideoBufferSizeProperty;

    static MFDecodingSettings()
    {
        ThresholdFrameCountProperty = ConfigureProperty<int, MFDecodingSettings>(nameof(ThresholdFrameCount))
            .DefaultValue(30)
            .Register();

        MaxVideoBufferSizeProperty = ConfigureProperty<int, MFDecodingSettings>(nameof(MaxVideoBufferSize))
            .DefaultValue(4)
            .Register();

        AffectsConfig<MFDecodingSettings>(ThresholdFrameCountProperty, MaxVideoBufferSizeProperty);
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
        Name = nameof(Strings.MaxVideoBufferSize),
        GroupName = nameof(Strings.Cache),
        ResourceType = typeof(Strings))]
    public int MaxVideoBufferSize
    {
        get => GetValue(MaxVideoBufferSizeProperty);
        set => SetValue(MaxVideoBufferSizeProperty, value);
    }
}
