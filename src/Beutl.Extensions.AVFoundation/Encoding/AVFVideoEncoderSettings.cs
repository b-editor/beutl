using System.ComponentModel.DataAnnotations;
using Beutl.Media.Encoding;

namespace Beutl.Extensions.AVFoundation.Encoding;

public sealed class AVFVideoEncoderSettings : VideoEncoderSettings
{
    public AVFVideoEncoderSettings()
        : base(-1, -1)
    {
        ScanProperties<AVFVideoEncoderSettings>();
    }

    [Display(Name = "Codec")]
    public IProperty<VideoCodec> Codec { get; } = new ValueOnlyProperty<VideoCodec>(VideoCodec.H264);

    [Display(Name = "JPEG Quality")]
    public IProperty<float> JPEGQuality { get; } = new ValueOnlyProperty<float>(-1);

    [Display(Name = "H.264 Profile/Level")]
    public IProperty<VideoProfileLevelH264> ProfileLevelH264 { get; } = new ValueOnlyProperty<VideoProfileLevelH264>(VideoProfileLevelH264.Default);

    [Display(Name = "Color Transfer",
        Description = "Pq / Hlg selects the HDR encoder path (HEVC Main10).")]
    /// <summary>
    /// Transfer characteristic embedded in the encoded stream. Setting Pq or Hlg selects the
    /// HDR encoder path (HEVC Main10 profile, 16bpc pixel pipeline, matching AVVideoColor
    /// properties).
    /// </summary>
    public IProperty<ColorTransferCharacteristic> ColorTransfer { get; } = new ValueOnlyProperty<ColorTransferCharacteristic>(ColorTransferCharacteristic.Default);

    [Display(Name = "Color Primaries")]
    public IProperty<ColorPrimariesType> ColorPrimaries { get; } = new ValueOnlyProperty<ColorPrimariesType>(ColorPrimariesType.Default);

    [Display(Name = "YCbCr Matrix")]
    public IProperty<YCbCrMatrixType> YCbCrMatrix { get; } = new ValueOnlyProperty<YCbCrMatrixType>(YCbCrMatrixType.Default);

    public bool IsHdr =>
        ColorTransfer.Value == ColorTransferCharacteristic.Pq ||
        ColorTransfer.Value == ColorTransferCharacteristic.Hlg;

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
