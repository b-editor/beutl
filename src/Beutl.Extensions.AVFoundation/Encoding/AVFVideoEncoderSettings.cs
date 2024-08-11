using Beutl.Media.Encoding;

namespace Beutl.Extensions.AVFoundation.Encoding;

public sealed class AVFVideoEncoderSettings : VideoEncoderSettings
{
    public static readonly CoreProperty<VideoCodec> CodecProperty;
    public static readonly CoreProperty<float> JPEGQualityProperty;
    public static readonly CoreProperty<VideoProfileLevelH264> ProfileLevelH264Property;

    static AVFVideoEncoderSettings()
    {
        CodecProperty = ConfigureProperty<VideoCodec, AVFVideoEncoderSettings>(nameof(Codec))
            .DefaultValue(VideoCodec.H264)
            .Register();

        JPEGQualityProperty = ConfigureProperty<float, AVFVideoEncoderSettings>(nameof(JPEGQuality))
            .DefaultValue(-1)
            .Register();

        ProfileLevelH264Property =
            ConfigureProperty<VideoProfileLevelH264, AVFVideoEncoderSettings>(nameof(ProfileLevelH264))
                .DefaultValue(VideoProfileLevelH264.Default)
                .Register();

        BitrateProperty.OverrideDefaultValue<AVFVideoEncoderSettings>(-1);
        KeyframeRateProperty.OverrideDefaultValue<AVFVideoEncoderSettings>(-1);
    }

    public VideoCodec Codec
    {
        get => GetValue(CodecProperty);
        set => SetValue(CodecProperty, value);
    }

    public float JPEGQuality
    {
        get => GetValue(JPEGQualityProperty);
        set => SetValue(JPEGQualityProperty, value);
    }

    public VideoProfileLevelH264 ProfileLevelH264
    {
        get => GetValue(ProfileLevelH264Property);
        set => SetValue(ProfileLevelH264Property, value);
    }

    public enum VideoCodec
    {
        Default = 0,
        H264 = 1,
        JPEG = 2,
    }

    public enum VideoProfileLevelH264
    {
        Default = 0,
        Baseline30 = 1,
        Baseline31 = 2,
        Baseline41 = 3,
        Main30 = 4,
        Main31 = 5,
        Main32 = 6,
        Main41 = 7,
    }
}
