using Beutl.Extensibility;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Decoding;
#else
namespace Beutl.Extensions.FFmpeg.Decoding;
#endif

public sealed class FFmpegDecodingSettings : ExtensionSettings
{
    public static readonly CoreProperty<int> ThreadCountProperty;
    public static readonly CoreProperty<AccelerationOptions> AccelerationProperty;
    public static readonly CoreProperty<bool> ForceSrgbGammaProperty;

    static FFmpegDecodingSettings()
    {
        ThreadCountProperty = ConfigureProperty<int, FFmpegDecodingSettings>(nameof(ThreadCount))
            .DefaultValue(-1)
            .Register();

        AccelerationProperty = ConfigureProperty<AccelerationOptions, FFmpegDecodingSettings>(nameof(Acceleration))
            .DefaultValue(AccelerationOptions.Software)
            .Register();

        ForceSrgbGammaProperty = ConfigureProperty<bool, FFmpegDecodingSettings>(nameof(ForceSrgbGamma))
            .DefaultValue(true)
            .Register();

        AffectsConfig<FFmpegDecodingSettings>(ThreadCountProperty, AccelerationProperty, ForceSrgbGammaProperty);
    }

    public int ThreadCount
    {
        get => GetValue(ThreadCountProperty);
        set => SetValue(ThreadCountProperty, value);
    }

    public AccelerationOptions Acceleration
    {
        get => GetValue(AccelerationProperty);
        set => SetValue(AccelerationProperty, value);
    }

    public bool ForceSrgbGamma
    {
        get => GetValue(ForceSrgbGammaProperty);
        set => SetValue(ForceSrgbGammaProperty, value);
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
