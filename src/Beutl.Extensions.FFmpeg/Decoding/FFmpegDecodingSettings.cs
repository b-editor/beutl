using Beutl.Extensibility;

using FFmpeg.AutoGen;

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
        FastBilinear = ffmpeg.SWS_FAST_BILINEAR,
        Bilinear = ffmpeg.SWS_BILINEAR,
        Bicubic = ffmpeg.SWS_BICUBIC,
        X = ffmpeg.SWS_X,
        Point = ffmpeg.SWS_POINT,
        Area = ffmpeg.SWS_AREA,
        Bicublin = ffmpeg.SWS_BICUBLIN,
        Gauss = ffmpeg.SWS_GAUSS,
        Sinc = ffmpeg.SWS_SINC,
        Lanczos = ffmpeg.SWS_LANCZOS,
        Spline = ffmpeg.SWS_SPLINE,
    }
}
