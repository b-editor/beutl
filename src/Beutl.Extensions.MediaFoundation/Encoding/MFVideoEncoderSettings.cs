using System.ComponentModel.DataAnnotations;
using Beutl.Media.Encoding;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Encoding;
#else
namespace Beutl.Extensions.MediaFoundation.Encoding;
#endif

public sealed class MFVideoEncoderSettings : VideoEncoderSettings
{
    public static readonly CoreProperty<VideoCodec> CodecProperty;
    public static readonly CoreProperty<H264ProfileType> H264ProfileProperty;
    public static readonly CoreProperty<HevcProfileType> HevcProfileProperty;
    public static readonly CoreProperty<ColorTransferCharacteristic> ColorTransferProperty;
    public static readonly CoreProperty<ColorPrimariesType> ColorPrimariesProperty;
    public static readonly CoreProperty<YCbCrMatrixType> YCbCrMatrixProperty;

    static MFVideoEncoderSettings()
    {
        CodecProperty = ConfigureProperty<VideoCodec, MFVideoEncoderSettings>(nameof(Codec))
            .DefaultValue(VideoCodec.H264)
            .Register();

        H264ProfileProperty = ConfigureProperty<H264ProfileType, MFVideoEncoderSettings>(nameof(H264Profile))
            .DefaultValue(H264ProfileType.High)
            .Register();

        HevcProfileProperty = ConfigureProperty<HevcProfileType, MFVideoEncoderSettings>(nameof(HevcProfile))
            .DefaultValue(HevcProfileType.Main)
            .Register();

        ColorTransferProperty = ConfigureProperty<ColorTransferCharacteristic, MFVideoEncoderSettings>(nameof(ColorTransfer))
            .DefaultValue(ColorTransferCharacteristic.Default)
            .Register();

        ColorPrimariesProperty = ConfigureProperty<ColorPrimariesType, MFVideoEncoderSettings>(nameof(ColorPrimaries))
            .DefaultValue(ColorPrimariesType.Default)
            .Register();

        YCbCrMatrixProperty = ConfigureProperty<YCbCrMatrixType, MFVideoEncoderSettings>(nameof(YCbCrMatrix))
            .DefaultValue(YCbCrMatrixType.Default)
            .Register();
    }

    [Display(Name = "Codec")]
    public VideoCodec Codec
    {
        get => GetValue(CodecProperty);
        set => SetValue(CodecProperty, value);
    }

    [Display(Name = "H.264 Profile")]
    public H264ProfileType H264Profile
    {
        get => GetValue(H264ProfileProperty);
        set => SetValue(H264ProfileProperty, value);
    }

    [Display(Name = "HEVC Profile")]
    public HevcProfileType HevcProfile
    {
        get => GetValue(HevcProfileProperty);
        set => SetValue(HevcProfileProperty, value);
    }

    /// <summary>
    /// Transfer characteristic embedded in the encoded stream. Setting Pq or Hlg selects the
    /// HDR encoder path (HEVC Main10 profile, 16bpc pixel pipeline, matching the output
    /// MF_MT_TRANSFER_FUNCTION tag).
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
        [Display(Name = "H.265 / HEVC")] HEVC = 2,
    }

    // Values match eAVEncH264VProfile on Windows. Media Foundation accepts these
    // directly via MF_MT_MPEG2_PROFILE, and they pin down what the decoder will
    // advertise to downstream consumers (browsers, NLE transcoders, etc.).
    public enum H264ProfileType
    {
        Baseline = 66,
        Main = 77,
        High = 100,
    }

    // Values match eAVEncH265VProfile. Main10 is *required* for any HDR output —
    // 8-bit HEVC cannot carry PQ/HLG without quantization artifacts.
    public enum HevcProfileType
    {
        Main = 1,
        Main10 = 2,
    }

    // Numeric values align with AVFVideoEncoderSettings.ColorTransferCharacteristic
    // so BitmapColorSpace lookup semantics stay consistent across backends.
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
