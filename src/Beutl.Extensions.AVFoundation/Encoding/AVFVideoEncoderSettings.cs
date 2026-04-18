using Beutl.Media.Encoding;

namespace Beutl.Extensions.AVFoundation.Encoding;

public sealed class AVFVideoEncoderSettings : VideoEncoderSettings
{
    public static readonly CoreProperty<VideoCodec> CodecProperty;
    public static readonly CoreProperty<float> JPEGQualityProperty;
    public static readonly CoreProperty<VideoProfileLevelH264> ProfileLevelH264Property;
    public static readonly CoreProperty<ColorTransferCharacteristic> ColorTransferProperty;
    public static readonly CoreProperty<ColorPrimariesType> ColorPrimariesProperty;
    public static readonly CoreProperty<YCbCrMatrixType> YCbCrMatrixProperty;

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

        ColorTransferProperty =
            ConfigureProperty<ColorTransferCharacteristic, AVFVideoEncoderSettings>(nameof(ColorTransfer))
                .DefaultValue(ColorTransferCharacteristic.Default)
                .Register();

        ColorPrimariesProperty =
            ConfigureProperty<ColorPrimariesType, AVFVideoEncoderSettings>(nameof(ColorPrimaries))
                .DefaultValue(ColorPrimariesType.Default)
                .Register();

        YCbCrMatrixProperty =
            ConfigureProperty<YCbCrMatrixType, AVFVideoEncoderSettings>(nameof(YCbCrMatrix))
                .DefaultValue(YCbCrMatrixType.Default)
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

    /// <summary>
    /// Transfer characteristic embedded in the encoded stream. Setting Pq or Hlg selects the
    /// HDR encoder path (HEVC Main10 profile, 16bpc pixel pipeline, matching AVVideoColor
    /// properties).
    /// </summary>
    public ColorTransferCharacteristic ColorTransfer
    {
        get => GetValue(ColorTransferProperty);
        set => SetValue(ColorTransferProperty, value);
    }

    public ColorPrimariesType ColorPrimaries
    {
        get => GetValue(ColorPrimariesProperty);
        set => SetValue(ColorPrimariesProperty, value);
    }

    public YCbCrMatrixType YCbCrMatrix
    {
        get => GetValue(YCbCrMatrixProperty);
        set => SetValue(YCbCrMatrixProperty, value);
    }

    public bool IsHdr =>
        ColorTransfer == ColorTransferCharacteristic.Pq ||
        ColorTransfer == ColorTransferCharacteristic.Hlg;

    public enum VideoCodec
    {
        Default = 0,
        H264 = 1,
        JPEG = 2,
        HEVC = 3,
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

    public enum ColorTransferCharacteristic
    {
        Default = 0,
        Srgb = 1,
        Linear = 2,
        Bt709 = 3,
        Pq = 4,
        Hlg = 5,
        Smpte240M = 9,
    }

    public enum ColorPrimariesType
    {
        Default = 0,
        Bt709 = 2,
        Rec2020 = 8,
        Dcip3 = 11,
        Smpte170M = 5,
    }

    public enum YCbCrMatrixType
    {
        Default = 0,
        Bt709 = 1,
        Bt601 = 2,
        Rec2020 = 3,
        Smpte240M = 4,
    }
}
