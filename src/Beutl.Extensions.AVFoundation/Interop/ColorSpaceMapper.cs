using Beutl.Media;

namespace Beutl.Extensions.AVFoundation.Interop;

// Mirrors Beutl.Extensions.FFmpeg/ColorSpaceHelper.cs so AVFoundation produces the same
// BitmapColorSpace shape as the FFmpeg path (HDR luminance scaling + gamut selection).
// Input is the tag values reported by the native layer (BeutlVideoInfo.TransferFunction /
// ColorPrimaries), which originate from CMFormatDescription extensions on the Swift side.
internal static class ColorSpaceMapper
{
    public static BitmapColorSpace BuildColorSpace(
        bool isHdr,
        BeutlTransferFunction transfer,
        BeutlColorPrimaries primaries)
    {
        BitmapColorSpaceTransferFn transferFn = MapTransfer(transfer);
        BitmapColorSpaceXyz gamut = MapPrimaries(primaries);

        if (isHdr)
        {
            float scale = GetEncodeLuminanceScale(transfer);
            if (scale != 1.0f)
            {
                gamut = gamut.Scale(scale);
            }
            return BitmapColorSpace.CreateRgb(transferFn, gamut);
        }

        if (transferFn == BitmapColorSpaceTransferFn.Srgb && gamut == BitmapColorSpaceXyz.Srgb)
            return BitmapColorSpace.Srgb;
        if (transferFn == BitmapColorSpaceTransferFn.Linear && gamut == BitmapColorSpaceXyz.Srgb)
            return BitmapColorSpace.LinearSrgb;
        return BitmapColorSpace.CreateRgb(transferFn, gamut);
    }

    // PQ: internal linear 1.0 maps to reference white (203 nit) out of 10000 nit peak.
    // HLG: match the FFmpeg path by using the SkiaSharp HLG EOTF at the reference code level.
    private static float GetEncodeLuminanceScale(BeutlTransferFunction transfer)
    {
        return transfer switch
        {
            BeutlTransferFunction.Pq => 10000f / 203f,
            BeutlTransferFunction.Hlg => ComputeHlgScale(),
            _ => 1.0f,
        };
    }

    private static float ComputeHlgScale()
    {
        const float hlgReferenceCode = 0.75f;
        float eotfValue = BitmapColorSpaceTransferFn.Hlg.Transform(hlgReferenceCode);
        if (eotfValue <= 0 || !float.IsFinite(eotfValue))
        {
            // Fallback to the OOTF γ=3 approximation the FFmpeg helper falls back to.
            return 18.0f;
        }
        return 1f / eotfValue;
    }

    private static BitmapColorSpaceTransferFn MapTransfer(BeutlTransferFunction transfer) => transfer switch
    {
        BeutlTransferFunction.Srgb => BitmapColorSpaceTransferFn.Srgb,
        BeutlTransferFunction.Linear => BitmapColorSpaceTransferFn.Linear,
        BeutlTransferFunction.Bt709 => BitmapColorSpaceTransferFn.Bt709,
        BeutlTransferFunction.Pq => BitmapColorSpaceTransferFn.Pq,
        BeutlTransferFunction.Hlg => BitmapColorSpaceTransferFn.Hlg,
        BeutlTransferFunction.Rec2020 => BitmapColorSpaceTransferFn.Rec2020,
        BeutlTransferFunction.TwoDotTwo => BitmapColorSpaceTransferFn.TwoDotTwo,
        BeutlTransferFunction.Gamma28 => BitmapColorSpaceTransferFn.Gamma28,
        BeutlTransferFunction.Smpte240M => BitmapColorSpaceTransferFn.Smpte240M,
        BeutlTransferFunction.Smpte428 => BitmapColorSpaceTransferFn.Smpte428,
        _ => BitmapColorSpaceTransferFn.Srgb,
    };

    private static BitmapColorSpaceXyz MapPrimaries(BeutlColorPrimaries primaries) => primaries switch
    {
        BeutlColorPrimaries.Srgb => BitmapColorSpaceXyz.Srgb,
        BeutlColorPrimaries.Bt709 => BitmapColorSpaceXyz.Bt709,
        BeutlColorPrimaries.Bt470M => BitmapColorSpaceXyz.Bt470M,
        BeutlColorPrimaries.Bt470BG => BitmapColorSpaceXyz.Bt470BG,
        BeutlColorPrimaries.Smpte170M => BitmapColorSpaceXyz.Smpte170M,
        BeutlColorPrimaries.Smpte240M => BitmapColorSpaceXyz.Smpte240M,
        BeutlColorPrimaries.Film => BitmapColorSpaceXyz.Film,
        BeutlColorPrimaries.Rec2020 => BitmapColorSpaceXyz.Rec2020,
        BeutlColorPrimaries.Xyz => BitmapColorSpaceXyz.Xyz,
        BeutlColorPrimaries.Smpte431 => BitmapColorSpaceXyz.Smpte431,
        BeutlColorPrimaries.Dcip3 => BitmapColorSpaceXyz.Dcip3,
        BeutlColorPrimaries.Ebu3213 => BitmapColorSpaceXyz.Ebu3213,
        _ => BitmapColorSpaceXyz.Srgb,
    };
}
