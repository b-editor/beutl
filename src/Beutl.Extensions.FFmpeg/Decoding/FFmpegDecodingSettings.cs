using Beutl.Extensibility;

using FFmpeg.AutoGen.Abstractions;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Decoding;
#else
namespace Beutl.Extensions.FFmpeg.Decoding;
#endif

public sealed class FFmpegDecodingSettings : ExtensionSettings
{
    public static readonly CoreProperty<ScalingAlgorithm> ScalingProperty;
    public static readonly CoreProperty<int> ThreadCountProperty;

    static FFmpegDecodingSettings()
    {
        ScalingProperty = ConfigureProperty<ScalingAlgorithm, FFmpegDecodingSettings>(nameof(Scaling))
            .DefaultValue(ScalingAlgorithm.Bicubic)
            .Register();

        ThreadCountProperty = ConfigureProperty<int, FFmpegDecodingSettings>(nameof(ThreadCount))
            .DefaultValue(-1)
            .Register();

        AffectsConfig<FFmpegDecodingSettings>(ScalingProperty, ThreadCountProperty);
    }

    public ScalingAlgorithm Scaling
    {
        get => GetValue(ScalingProperty);
        set => SetValue(ScalingProperty, value);
    }

    public int ThreadCount
    {
        get => GetValue(ThreadCountProperty);
        set => SetValue(ThreadCountProperty, value);
    }

    public enum ScalingAlgorithm
    {
        FastBilinear = SwsFlags.SWS_FAST_BILINEAR,
        Bilinear = SwsFlags.SWS_BILINEAR,
        Bicubic = SwsFlags.SWS_BICUBIC,
        X = SwsFlags.SWS_X,
        Point = SwsFlags.SWS_POINT,
        Area = SwsFlags.SWS_AREA,
        Bicublin = SwsFlags.SWS_BICUBLIN,
        Gauss = SwsFlags.SWS_GAUSS,
        Sinc = SwsFlags.SWS_SINC,
        Lanczos = SwsFlags.SWS_LANCZOS,
        Spline = SwsFlags.SWS_SPLINE,
    }
}
