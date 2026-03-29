using Beutl.Media;
using FFmpeg.AutoGen.Abstractions;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg;
#else
namespace Beutl.Extensions.FFmpeg;
#endif

internal static class ColorSpaceHelper
{
    public static bool IsHdrTransfer(AVColorTransferCharacteristic trc)
    {
        return trc is AVColorTransferCharacteristic.AVCOL_TRC_SMPTE2084
            or AVColorTransferCharacteristic.AVCOL_TRC_ARIB_STD_B67;
    }

    public static BitmapColorSpace BuildTargetColorSpace(
        AVColorTransferCharacteristic trc, AVColorPrimaries primaries)
    {
        var transferFn = GetTransferFunction(trc);
        var gamut = GetBitmapColorSpaceXyz(primaries);

        if (transferFn == BitmapColorSpaceTransferFn.Srgb && gamut == BitmapColorSpaceXyz.Srgb)
            return BitmapColorSpace.Srgb;

        if (transferFn == BitmapColorSpaceTransferFn.Linear && gamut == BitmapColorSpaceXyz.Srgb)
            return BitmapColorSpace.LinearSrgb;

        return BitmapColorSpace.CreateRgb(transferFn, gamut);
    }

    /// <summary>
    /// HDR用の色空間を構築する（エンコード/デコード共通）。
    /// HDR (PQ/HLG) の場合、内部リニア空間の1.0がリファレンス白（203 nit）に
    /// マッピングされるよう、ガマット行列に輝度スケーリングを組み込む。
    /// </summary>
    public static BitmapColorSpace BuildHdrColorSpace(
        AVColorTransferCharacteristic trc, AVColorPrimaries primaries)
    {
        var transferFn = GetTransferFunction(trc);
        var gamut = GetBitmapColorSpaceXyz(primaries);

        float scale = GetEncodeLuminanceScale(trc);
        if (scale != 1.0f)
        {
            gamut = gamut.Scale(scale);
        }

        return BitmapColorSpace.CreateRgb(transferFn, gamut);
    }

    private static float GetEncodeLuminanceScale(AVColorTransferCharacteristic trc)
    {
        return trc switch
        {
            AVColorTransferCharacteristic.AVCOL_TRC_SMPTE2084 => GetPqLuminanceScale(),
            AVColorTransferCharacteristic.AVCOL_TRC_ARIB_STD_B67 => GetHlgLuminanceScale(),
            _ => 1.0f
        };
    }

    private static float GetPqLuminanceScale()
    {
        // PQ: 内部リニア1.0 = リファレンス白 (203 nit)
        // PQピーク = 10000 nit
        // ガマット行列をスケーリングして、逆行列が内部1.0をPQリニア 203/10000 にマッピングするようにする
        const float referenceWhiteNits = 203f;
        const float pqPeakNits = 10000f;
        return pqPeakNits / referenceWhiteNits;
    }

    private static float GetHlgLuminanceScale()
    {
        // HLG: 内部リニア1.0 = リファレンス白
        // BT.2100でのHLGリファレンス白はコードレベル ~0.75
        // SkiaのHLG EOTF（OOTF含む）でリファレンスコードの表示リニア値を取得し、
        // スケールファクターを計算する
        const float hlgReferenceCode = 0.75f;
        float eotfValue = BitmapColorSpaceTransferFn.Hlg.Transform(hlgReferenceCode);
        if (eotfValue <= 0 || !float.IsFinite(eotfValue))
        {
            // フォールバック: OOTF γ=3 での近似値
            return 18.0f;
        }

        return 1f / eotfValue;
    }

    public static BitmapColorSpaceTransferFn GetTransferFunction(AVColorTransferCharacteristic trc)
    {
        return trc switch
        {
            AVColorTransferCharacteristic.AVCOL_TRC_LINEAR => BitmapColorSpaceTransferFn.Linear,
            AVColorTransferCharacteristic.AVCOL_TRC_GAMMA22 => BitmapColorSpaceTransferFn.TwoDotTwo,
            AVColorTransferCharacteristic.AVCOL_TRC_BT2020_10 or
                AVColorTransferCharacteristic.AVCOL_TRC_BT2020_12 => BitmapColorSpaceTransferFn.Rec2020,
            AVColorTransferCharacteristic.AVCOL_TRC_SMPTE2084 => BitmapColorSpaceTransferFn.Pq,
            AVColorTransferCharacteristic.AVCOL_TRC_ARIB_STD_B67 => BitmapColorSpaceTransferFn.Hlg,
            AVColorTransferCharacteristic.AVCOL_TRC_BT709 or
                AVColorTransferCharacteristic.AVCOL_TRC_SMPTE170M or
                AVColorTransferCharacteristic.AVCOL_TRC_IEC61966_2_4 or
                AVColorTransferCharacteristic.AVCOL_TRC_BT1361_ECG => BitmapColorSpaceTransferFn.Bt709,
            AVColorTransferCharacteristic.AVCOL_TRC_GAMMA28 => BitmapColorSpaceTransferFn.Gamma28,
            AVColorTransferCharacteristic.AVCOL_TRC_SMPTE240M => BitmapColorSpaceTransferFn.Smpte240M,
            AVColorTransferCharacteristic.AVCOL_TRC_SMPTE428 => BitmapColorSpaceTransferFn.Smpte428,
            _ => BitmapColorSpaceTransferFn.Srgb
        };
    }

    public static BitmapColorSpaceXyz GetBitmapColorSpaceXyz(AVColorPrimaries primaries)
    {
        return primaries switch
        {
            AVColorPrimaries.AVCOL_PRI_BT709 => BitmapColorSpaceXyz.Bt709,
            AVColorPrimaries.AVCOL_PRI_BT470M => BitmapColorSpaceXyz.Bt470M,
            AVColorPrimaries.AVCOL_PRI_BT470BG => BitmapColorSpaceXyz.Bt470BG,
            AVColorPrimaries.AVCOL_PRI_SMPTE170M => BitmapColorSpaceXyz.Smpte170M,
            AVColorPrimaries.AVCOL_PRI_SMPTE240M => BitmapColorSpaceXyz.Smpte240M,
            AVColorPrimaries.AVCOL_PRI_FILM => BitmapColorSpaceXyz.Film,
            AVColorPrimaries.AVCOL_PRI_BT2020 => BitmapColorSpaceXyz.Rec2020,
            AVColorPrimaries.AVCOL_PRI_SMPTE428 => BitmapColorSpaceXyz.Xyz,
            AVColorPrimaries.AVCOL_PRI_SMPTE431 => BitmapColorSpaceXyz.Smpte431,
            AVColorPrimaries.AVCOL_PRI_SMPTE432 => BitmapColorSpaceXyz.Dcip3,
            AVColorPrimaries.AVCOL_PRI_EBU3213 => BitmapColorSpaceXyz.Ebu3213,
            _ => BitmapColorSpaceXyz.Srgb
        };
    }
}
