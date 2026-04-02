namespace Beutl.FFmpegIpc;

/// <summary>
/// よく使うAVPixelFormat値の定数。FFmpegVideoEncoderSettings.Formatで使用。
/// </summary>
public static class FFPixelFormat
{
    public const int None = -1;
    public const int YUV420P = 0;
    public const int YUV420P10LE = 62;
}

/// <summary>
/// FFmpegのAVColorPrimaries互換enum。int値はFFmpeg定数と一致。
/// </summary>
public enum FFColorPrimaries
{
    BT709 = 1,
    UNSPECIFIED = 2,
    BT470M = 4,
    BT470BG = 5,
    SMPTE170M = 6,
    SMPTE240M = 7,
    FILM = 8,
    BT2020 = 9,
    SMPTE428 = 10,
    SMPTEST428_1 = 10,
    SMPTE431 = 11,
    SMPTE432 = 12,
    EBU3213 = 22,
    JEDEC_P22 = 22
}

/// <summary>
/// FFmpegのAVColorTransferCharacteristic互換enum。int値はFFmpeg定数と一致。
/// </summary>
public enum FFColorTransfer
{
    BT709 = 1,
    UNSPECIFIED = 2,
    GAMMA22 = 4,
    GAMMA28 = 5,
    SMPTE170M = 6,
    SMPTE240M = 7,
    LINEAR = 8,
    LOG = 9,
    LOG_SQRT = 10,
    IEC61966_2_4 = 11,
    BT1361_ECG = 12,
    IEC61966_2_1 = 13,
    BT2020_10 = 14,
    BT2020_12 = 15,
    SMPTE2084 = 16,
    SMPTEST2084 = 16,
    SMPTE428 = 17,
    SMPTEST428_1 = 17,
    ARIB_STD_B67 = 18
}

/// <summary>
/// FFmpegのAVColorSpace互換enum。int値はFFmpeg定数と一致。
/// </summary>
public enum FFColorSpace
{
    RGB = 0,
    BT709 = 1,
    UNSPECIFIED = 2,
    FCC = 4,
    BT470BG = 5,
    SMPTE170M = 6,
    SMPTE240M = 7,
    YCGCO = 8,
    YCOCG = 8,
    BT2020_NCL = 9,
    BT2020_CL = 10,
    SMPTE2085 = 11,
    CHROMA_DERIVED_NCL = 12,
    CHROMA_DERIVED_CL = 13,
    ICTCP = 14,
    IPT_C2 = 15,
    YCGCO_RE = 16,
    YCGCO_RO = 17
}

/// <summary>
/// FFmpegのAVColorRange互換enum。int値はFFmpeg定数と一致。
/// </summary>
public enum FFColorRange
{
    UNSPECIFIED = 0,
    MPEG = 1,
    JPEG = 2,
}
