using Beutl.Media;
using FFmpeg.AutoGen.Abstractions;

namespace Beutl.FFmpegWorker;

// Thin FFmpeg layer over the shared Beutl.Media.BitmapColorSpaceMapping: it only converts the
// native FFmpeg transfer/primaries tags to Beutl tags and delegates, so the HDR luminance-scaling
// strategy lives in exactly one place across all encode/decode backends.
internal static class ColorSpaceHelper
{
    public static bool IsHdrTransfer(AVColorTransferCharacteristic trc)
        => BitmapColorSpaceMapping.IsHdrTransfer(ToTransfer(trc));

    public static BitmapColorSpace BuildTargetColorSpace(
        AVColorTransferCharacteristic trc, AVColorPrimaries primaries)
        => BitmapColorSpaceMapping.BuildTargetColorSpace(ToTransfer(trc), ToPrimaries(primaries));

    public static BitmapColorSpace BuildHdrColorSpace(
        AVColorTransferCharacteristic trc, AVColorPrimaries primaries)
        => BitmapColorSpaceMapping.BuildHdrColorSpace(ToTransfer(trc), ToPrimaries(primaries));

    public static BitmapColorSpaceTransferFn GetTransferFunction(AVColorTransferCharacteristic trc)
        => BitmapColorSpaceMapping.GetTransferFunction(ToTransfer(trc));

    public static BitmapColorSpaceXyz GetBitmapColorSpaceXyz(AVColorPrimaries primaries)
        => BitmapColorSpaceMapping.GetPrimaries(ToPrimaries(primaries));

    private static BitmapColorTransfer ToTransfer(AVColorTransferCharacteristic trc)
    {
        return trc switch
        {
            AVColorTransferCharacteristic.AVCOL_TRC_LINEAR => BitmapColorTransfer.Linear,
            AVColorTransferCharacteristic.AVCOL_TRC_GAMMA22 => BitmapColorTransfer.TwoDotTwo,
            AVColorTransferCharacteristic.AVCOL_TRC_BT2020_10 or
                AVColorTransferCharacteristic.AVCOL_TRC_BT2020_12 => BitmapColorTransfer.Rec2020,
            AVColorTransferCharacteristic.AVCOL_TRC_SMPTE2084 => BitmapColorTransfer.Pq,
            AVColorTransferCharacteristic.AVCOL_TRC_ARIB_STD_B67 => BitmapColorTransfer.Hlg,
            AVColorTransferCharacteristic.AVCOL_TRC_BT709 or
                AVColorTransferCharacteristic.AVCOL_TRC_SMPTE170M or
                AVColorTransferCharacteristic.AVCOL_TRC_IEC61966_2_4 or
                AVColorTransferCharacteristic.AVCOL_TRC_BT1361_ECG => BitmapColorTransfer.Bt709,
            AVColorTransferCharacteristic.AVCOL_TRC_GAMMA28 => BitmapColorTransfer.Gamma28,
            AVColorTransferCharacteristic.AVCOL_TRC_SMPTE240M => BitmapColorTransfer.Smpte240M,
            AVColorTransferCharacteristic.AVCOL_TRC_SMPTE428 => BitmapColorTransfer.Smpte428,
            _ => BitmapColorTransfer.Unknown
        };
    }

    private static BitmapColorPrimaries ToPrimaries(AVColorPrimaries primaries)
    {
        return primaries switch
        {
            AVColorPrimaries.AVCOL_PRI_BT709 => BitmapColorPrimaries.Bt709,
            AVColorPrimaries.AVCOL_PRI_BT470M => BitmapColorPrimaries.Bt470M,
            AVColorPrimaries.AVCOL_PRI_BT470BG => BitmapColorPrimaries.Bt470BG,
            AVColorPrimaries.AVCOL_PRI_SMPTE170M => BitmapColorPrimaries.Smpte170M,
            AVColorPrimaries.AVCOL_PRI_SMPTE240M => BitmapColorPrimaries.Smpte240M,
            AVColorPrimaries.AVCOL_PRI_FILM => BitmapColorPrimaries.Film,
            AVColorPrimaries.AVCOL_PRI_BT2020 => BitmapColorPrimaries.Rec2020,
            AVColorPrimaries.AVCOL_PRI_SMPTE428 => BitmapColorPrimaries.Xyz,
            AVColorPrimaries.AVCOL_PRI_SMPTE431 => BitmapColorPrimaries.Smpte431,
            AVColorPrimaries.AVCOL_PRI_SMPTE432 => BitmapColorPrimaries.Dcip3,
            AVColorPrimaries.AVCOL_PRI_EBU3213 => BitmapColorPrimaries.Ebu3213,
            _ => BitmapColorPrimaries.Unknown
        };
    }
}
