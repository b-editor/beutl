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
    public static readonly CoreProperty<HardwareAccelerationMode> HardwareAccelerationProperty;
    public static readonly CoreProperty<bool> ForceSrgbGammaProperty;
    public static readonly CoreProperty<int> ThresholdFrameCountProperty;
    public static readonly CoreProperty<int> ThresholdSampleCountProperty;
    public static readonly CoreProperty<int> MaxVideoBufferSizeProperty;
    public static readonly CoreProperty<int> MaxAudioBufferSizeProperty;

    static MFDecodingSettings()
    {
        UseDXVA2Property = ConfigureProperty<bool, MFDecodingSettings>(nameof(UseDXVA2))
            .DefaultValue(true)
            .Register();

        HardwareAccelerationProperty = ConfigureProperty<HardwareAccelerationMode, MFDecodingSettings>(nameof(HardwareAcceleration))
            .DefaultValue(HardwareAccelerationMode.Auto)
            .Register();

        ForceSrgbGammaProperty = ConfigureProperty<bool, MFDecodingSettings>(nameof(ForceSrgbGamma))
            .DefaultValue(false)
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

        AffectsConfig<MFDecodingSettings>(UseDXVA2Property, HardwareAccelerationProperty);
    }

    [Display(Name = nameof(Strings.UseDXVA2), Description = nameof(Strings.UseDXVA2_Description), ResourceType = typeof(Strings))]
    public bool UseDXVA2
    {
        get => GetValue(UseDXVA2Property);
        set => SetValue(UseDXVA2Property, value);
    }

    [Display(
        Name = nameof(Strings.HardwareAcceleration),
        Description = nameof(Strings.HardwareAcceleration_Description),
        ResourceType = typeof(Strings))]
    public HardwareAccelerationMode HardwareAcceleration
    {
        get => GetValue(HardwareAccelerationProperty);
        set => SetValue(HardwareAccelerationProperty, value);
    }

    [Display(
        Name = nameof(Strings.ForceSrgbGamma),
        Description = nameof(Strings.ForceSrgbGamma_Description),
        ResourceType = typeof(Strings))]
    public bool ForceSrgbGamma
    {
        get => GetValue(ForceSrgbGammaProperty);
        set => SetValue(ForceSrgbGammaProperty, value);
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

public enum HardwareAccelerationMode
{
    // Software decode only.
    None = 0,

    // Let the decoder pick D3D11VA on Windows 10+ (preferred) and fall back to DXVA2.
    Auto = 1,

    // Force DXVA2 — older but broadest driver coverage.
    DXVA2 = 2,

    // Force D3D11VA — required for HDR / 10-bit decode paths.
    D3D11VA = 3,
}
