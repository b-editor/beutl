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
        ThreadCount.ValueChanged += (_, _) => OnChanged();
        Acceleration.ValueChanged += (_, _) => OnChanged();
    }

    public IProperty<int> ThreadCount { get; } = Property.CreateValue(-1);

    public IProperty<AccelerationOptions> Acceleration { get; } = Property.CreateValue(AccelerationOptions.Software);

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
