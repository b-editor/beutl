﻿using Beutl.Extensibility;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

public sealed class FFmpegEncodingSettings : ExtensionSettings
{
    public static readonly CoreProperty<int> ThreadCountProperty;
    public static readonly CoreProperty<AccelerationOptions> AccelerationProperty;

    static FFmpegEncodingSettings()
    {
        ThreadCountProperty = ConfigureProperty<int, FFmpegEncodingSettings>(nameof(ThreadCount))
            .DefaultValue(-1)
            .Register();

        AccelerationProperty = ConfigureProperty<AccelerationOptions, FFmpegEncodingSettings>(nameof(Acceleration))
            .DefaultValue(AccelerationOptions.Software)
            .Register();

        AffectsConfig<FFmpegEncodingSettings>(ThreadCountProperty, AccelerationProperty);
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
