using System.ComponentModel.DataAnnotations;
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

    [Display(Name = "Codec")]
    public VideoCodec Codec
    {
        get => GetValue(CodecProperty);
        set => SetValue(CodecProperty, value);
    }

    [Display(Name = "JPEG Quality")]
    public float JPEGQuality
    {
        get => GetValue(JPEGQualityProperty);
        set => SetValue(JPEGQualityProperty, value);
    }

    [Display(Name = "H.264 Profile/Level")]
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
    [Display(Name = "Color Transfer",
        Description = "Pq / Hlg selects the HDR encoder path (HEVC Main10).")]
    public ColorTransferCharacteristic ColorTransfer
    {
        get => GetValue(ColorTransferProperty);
        set => SetValue(ColorTransferProperty, value);
    }

    [Display(Name = "Color Primaries")]
    public ColorPrimariesType ColorPrimaries
    {
        get => GetValue(ColorPrimariesProperty);
        set => SetValue(ColorPrimariesProperty, value);
    }

    [Display(Name = "YCbCr Matrix")]
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
        [Display(Name = "Default (H.264)")] Default = 0,
        [Display(Name = "H.264 / AVC")] H264 = 1,
        [Display(Name = "JPEG")] JPEG = 2,
        [Display(Name = "H.265 / HEVC")] HEVC = 3,
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
        [Display(Name = "Default (SDR)")] Default = 0,
        [Display(Name = "sRGB")] Srgb = 1,
        [Display(Name = "Linear")] Linear = 2,
        [Display(Name = "BT.709")] Bt709 = 3,
        [Display(Name = "PQ (HDR10 / SMPTE ST 2084)")] Pq = 4,
        [Display(Name = "HLG (ITU-R BT.2100)")] Hlg = 5,
        [Display(Name = "SMPTE 240M")] Smpte240M = 9,
    }

    public enum ColorPrimariesType
    {
        [Display(Name = "Default")] Default = 0,
        [Display(Name = "BT.709")] Bt709 = 2,
        [Display(Name = "Rec.2020 / BT.2020")] Rec2020 = 8,
        [Display(Name = "DCI-P3 (D65)")] Dcip3 = 11,
        [Display(Name = "SMPTE 170M")] Smpte170M = 5,
    }

    public enum YCbCrMatrixType
    {
        [Display(Name = "Default")] Default = 0,
        [Display(Name = "BT.709")] Bt709 = 1,
        [Display(Name = "BT.601")] Bt601 = 2,
        [Display(Name = "Rec.2020 / BT.2020")] Rec2020 = 3,
        [Display(Name = "SMPTE 240M")] Smpte240M = 4,
    }
}
