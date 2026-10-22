using Beutl.Extensibility;

#if BEUTL_FFMPEG_WORKER
namespace Beutl.FFmpegWorker.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

public sealed class FFmpegEncodingSettings : ExtensionSettings
{
    public FFmpegEncodingSettings()
    {
        ScanProperties<FFmpegEncodingSettings>();
        ThreadCountProperty.ValueChanged += (_, _) => OnChanged();
        AccelerationProperty.ValueChanged += (_, _) => OnChanged();
    }

    public IProperty<int> ThreadCountProperty { get; } = new ValueOnlyProperty<int>(-1);

    public IProperty<AccelerationOptions> AccelerationProperty { get; } = new ValueOnlyProperty<AccelerationOptions>(AccelerationOptions.Software);

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
