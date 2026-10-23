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
        ThresholdFrameCount.ValueChanged += (_, _) => OnChanged();
        MaxVideoBufferSize.ValueChanged += (_, _) => OnChanged();
    }

    [Range(1, int.MaxValue)]
    [Display(
        Name = nameof(Strings.SeekThresholdInVideoStream),
        Description = nameof(Strings.SeekThresholdInVideoStream_Description),
        ResourceType = typeof(Strings))]
    public IProperty<int> ThresholdFrameCount { get; } = Property.CreateValue(30);

    [Range(1, int.MaxValue)]
    [Display(
        Name = nameof(Strings.MaxVideoBufferSize),
        GroupName = nameof(Strings.Cache),
        ResourceType = typeof(Strings))]
    public IProperty<int> MaxVideoBufferSize { get; } = Property.CreateValue(4);
}
