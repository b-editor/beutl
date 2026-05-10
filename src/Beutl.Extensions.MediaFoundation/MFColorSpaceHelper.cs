using Beutl.Media;
using Vortice.MediaFoundation;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation;
#else
namespace Beutl.Extensions.MediaFoundation;
#endif

// Mirrors Beutl.Extensions.FFmpeg/ColorSpaceHelper.cs and
// Beutl.Extensions.AVFoundation/Interop/ColorSpaceMapper.cs so that
// Media Foundation produces the same BitmapColorSpace shape (HDR luminance
// scaling + gamut selection) as the other two backends. Input is the
// Media Foundation enum values pulled from IMFMediaType attributes
// (MF_MT_TRANSFER_FUNCTION / MF_MT_VIDEO_PRIMARIES / MF_MT_YUV_MATRIX).
internal static class MFColorSpaceHelper
{
    public static bool IsHdrTransfer(VideoTransferFunction trc)
    {
        return trc is VideoTransferFunction.Func2084 or VideoTransferFunction.FuncHlg;
    }

    public static BitmapColorSpace BuildTargetColorSpace(
        VideoTransferFunction trc, VideoPrimaries primaries)
    {
        BitmapColorSpaceTransferFn transferFn = GetTransferFunction(trc);
        BitmapColorSpaceXyz gamut = GetBitmapColorSpaceXyz(primaries);

        if (transferFn == BitmapColorSpaceTransferFn.Srgb && gamut == BitmapColorSpaceXyz.Srgb)
            return BitmapColorSpace.Srgb;

        if (transferFn == BitmapColorSpaceTransferFn.Linear && gamut == BitmapColorSpaceXyz.Srgb)
            return BitmapColorSpace.LinearSrgb;

        return BitmapColorSpace.CreateRgb(transferFn, gamut);
    }

    // HDR: PQ (SMPTE ST 2084) or HLG (ARIB STD B67 / ITU-R BT.2100).
    // The gamut matrix is scaled so that internal linear 1.0 maps to the
    // reference white (203 nit for PQ; HLG reference code 0.75 for HLG).
    // Matches FFmpeg ColorSpaceHelper.BuildHdrColorSpace / AVF ColorSpaceMapper.
    public static BitmapColorSpace BuildHdrColorSpace(
        VideoTransferFunction trc, VideoPrimaries primaries)
    {
        BitmapColorSpaceTransferFn transferFn = GetTransferFunction(trc);
        // When the stream omits primaries but claims PQ/HLG, Rec.2020 is the
        // spec-mandated default for HDR10/HLG — falling back to sRGB here
        // would produce wrong colors after Skia applies the gamut matrix.
        BitmapColorSpaceXyz gamut = primaries == VideoPrimaries.Unknown
            ? BitmapColorSpaceXyz.Rec2020
            : GetBitmapColorSpaceXyz(primaries);

        float scale = GetEncodeLuminanceScale(trc);
        if (scale != 1.0f)
        {
            gamut = gamut.Scale(scale);
        }

        return BitmapColorSpace.CreateRgb(transferFn, gamut);
    }

    private static float GetEncodeLuminanceScale(VideoTransferFunction trc)
    {
        return trc switch
        {
            VideoTransferFunction.Func2084 => GetPqLuminanceScale(),
            VideoTransferFunction.FuncHlg => GetHlgLuminanceScale(),
            _ => 1.0f,
        };
    }

    private static float GetPqLuminanceScale()
    {
        // PQ: internal linear 1.0 = reference white (203 nit), peak = 10000 nit.
        const float referenceWhiteNits = 203f;
        const float pqPeakNits = 10000f;
        return pqPeakNits / referenceWhiteNits;
    }

    private static float GetHlgLuminanceScale()
    {
        // HLG reference white sits at code level ~0.75 (BT.2100). Ask Skia's
        // HLG EOTF what that maps to in display-linear and take the inverse.
        const float hlgReferenceCode = 0.75f;
        float eotfValue = BitmapColorSpaceTransferFn.Hlg.Transform(hlgReferenceCode);
        if (eotfValue <= 0 || !float.IsFinite(eotfValue))
        {
            // OOTF γ=3 fallback — matches FFmpeg helper.
            return 18.0f;
        }

        return 1f / eotfValue;
    }

    public static BitmapColorSpaceTransferFn GetTransferFunction(VideoTransferFunction trc)
    {
        return trc switch
        {
            VideoTransferFunction.Func10 => BitmapColorSpaceTransferFn.Linear,
            VideoTransferFunction.Func22 => BitmapColorSpaceTransferFn.TwoDotTwo,
            VideoTransferFunction.Func2020 or
                VideoTransferFunction.Func2020Const => BitmapColorSpaceTransferFn.Rec2020,
            VideoTransferFunction.Func2084 => BitmapColorSpaceTransferFn.Pq,
            VideoTransferFunction.FuncHlg => BitmapColorSpaceTransferFn.Hlg,
            VideoTransferFunction.Func709 or
                VideoTransferFunction.Func709Sym or
                VideoTransferFunction.FuncBt1361Ecg => BitmapColorSpaceTransferFn.Bt709,
            VideoTransferFunction.Func28 => BitmapColorSpaceTransferFn.Gamma28,
            VideoTransferFunction.Func240m => BitmapColorSpaceTransferFn.Smpte240M,
            VideoTransferFunction.FuncSmpte428 => BitmapColorSpaceTransferFn.Smpte428,
            VideoTransferFunction.FuncSRGB => BitmapColorSpaceTransferFn.Srgb,
            _ => BitmapColorSpaceTransferFn.Srgb,
        };
    }

    public static BitmapColorSpaceXyz GetBitmapColorSpaceXyz(VideoPrimaries primaries)
    {
        return primaries switch
        {
            VideoPrimaries.Bt709 => BitmapColorSpaceXyz.Bt709,
            VideoPrimaries.Bt4702SysM => BitmapColorSpaceXyz.Bt470M,
            VideoPrimaries.Bt4702SysBG => BitmapColorSpaceXyz.Bt470BG,
            VideoPrimaries.Smpte170m or VideoPrimaries.SmpteC => BitmapColorSpaceXyz.Smpte170M,
            VideoPrimaries.Smpte240m => BitmapColorSpaceXyz.Smpte240M,
            VideoPrimaries.Ebu3213 => BitmapColorSpaceXyz.Ebu3213,
            VideoPrimaries.Bt2020 => BitmapColorSpaceXyz.Rec2020,
            VideoPrimaries.Xyz => BitmapColorSpaceXyz.Xyz,
            // Naming is deliberately mismatched: the MF VideoPrimaries names
            // describe the source primaries set, while BitmapColorSpaceXyz names
            // describe the underlying SkColorSpace (DCI-P3 white = SMPTE-431,
            // D65 white = Skia's "DisplayP3" which it calls Dcip3).
            VideoPrimaries.DciP3 => BitmapColorSpaceXyz.Smpte431,
            VideoPrimaries.DisplayP3 => BitmapColorSpaceXyz.Dcip3,
            _ => BitmapColorSpaceXyz.Srgb,
        };
    }
}
