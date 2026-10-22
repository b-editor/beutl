using Beutl.Extensibility;

#if BEUTL_FFMPEG_WORKER
namespace Beutl.FFmpegWorker.Decoding;
#else
namespace Beutl.Extensions.FFmpeg.Decoding;
#endif

public sealed class FFmpegDecodingSettings : ExtensionSettings
{
    public FFmpegDecodingSettings()
    {
        ScanProperties<FFmpegDecodingSettings>();
        ThreadCountProperty.ValueChanged += (_, _) => OnChanged();
        AccelerationProperty.ValueChanged += (_, _) => OnChanged();
        ForceSrgbGammaProperty.ValueChanged += (_, _) => OnChanged();
    }

    public IProperty<int> ThreadCountProperty { get; } = new ValueOnlyProperty<int>(-1);

    public IProperty<AccelerationOptions> AccelerationProperty { get; } = new ValueOnlyProperty<AccelerationOptions>(AccelerationOptions.Software);

    public IProperty<bool> ForceSrgbGammaProperty { get; } = new ValueOnlyProperty<bool>(true);

    public int ThreadCount
    {
        get => ThreadCountProperty.Value;
        set => ThreadCountProperty.Value = value;
    }

    public AccelerationOptions Acceleration
    {
        get => AccelerationProperty.Value;
        set => AccelerationProperty.Value = value;
    }

    public bool ForceSrgbGamma
    {
        get => ForceSrgbGammaProperty.Value;
        set => ForceSrgbGammaProperty.Value = value;
    }

    public enum AccelerationOptions
    {
        Software,
        Auto,
        VDPAU,
        CUDA,
        VAAPI,
        DXVA2,
        QSV,
        VideoToolbox,
        D3D11VA,
        DRM,
        OpenCL,
        MediaCodec,
        Vulkan
    }
}
