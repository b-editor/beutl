using Beutl.Media;
using FFmpeg.AutoGen.Abstractions;

namespace Beutl.FFmpegWorker;

// Thin FFmpeg layer over the shared Beutl.Media.BitmapColorSpaceMapping: it only converts between the
// native FFmpeg color tags and Beutl tags and delegates, so the HDR luminance-scaling strategy lives in
// exactly one place across all encode/decode backends.
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

    // Only the values VideoColorDefaults returns need a mapping; the rest stay unspecified.
    public static AVColorTransferCharacteristic ToFFmpeg(BitmapColorTransfer transfer) => transfer switch
    {
        BitmapColorTransfer.Srgb => AVColorTransferCharacteristic.AVCOL_TRC_IEC61966_2_1,
        BitmapColorTransfer.Bt709 => AVColorTransferCharacteristic.AVCOL_TRC_BT709,
        _ => AVColorTransferCharacteristic.AVCOL_TRC_UNSPECIFIED,
    };

    public static AVColorPrimaries ToFFmpeg(BitmapColorPrimaries primaries) => primaries switch
    {
        BitmapColorPrimaries.Bt709 => AVColorPrimaries.AVCOL_PRI_BT709,
        BitmapColorPrimaries.Rec2020 => AVColorPrimaries.AVCOL_PRI_BT2020,
        _ => AVColorPrimaries.AVCOL_PRI_UNSPECIFIED,
    };

    public static AVColorSpace ToFFmpeg(VideoColorMatrix matrix) => matrix switch
    {
        VideoColorMatrix.Identity => AVColorSpace.AVCOL_SPC_RGB,
        VideoColorMatrix.Bt709 => AVColorSpace.AVCOL_SPC_BT709,
        VideoColorMatrix.Bt601 => AVColorSpace.AVCOL_SPC_SMPTE170M,
        VideoColorMatrix.Bt2020 => AVColorSpace.AVCOL_SPC_BT2020_NCL,
        _ => AVColorSpace.AVCOL_SPC_UNSPECIFIED,
    };

    public static AVColorRange ToFFmpeg(VideoColorRange range) => range switch
    {
        VideoColorRange.Limited => AVColorRange.AVCOL_RANGE_MPEG,
        VideoColorRange.Full => AVColorRange.AVCOL_RANGE_JPEG,
        _ => AVColorRange.AVCOL_RANGE_UNSPECIFIED,
    };
}
