namespace Beutl.Media;

/// <summary>
/// Backend-independent SDR/HDR color-space mapping shared by every encode/decode backend
/// (FFmpeg, AVFoundation). Each backend converts its native transfer/primaries
/// tags to <see cref="BitmapColorTransfer"/> / <see cref="BitmapColorPrimaries"/> and calls in
/// here, so the HDR luminance-scaling strategy (PQ: reference white 203 nit out of a 10000 nit
/// peak; HLG: scale from the BT.2100 reference code level, with an OOTF γ=3 fallback) is defined
/// in exactly one place instead of being copy-pasted per backend.
/// </summary>
public static class BitmapColorSpaceMapping
{
    private const float PqReferenceWhiteNits = 203f;
    private const float PqPeakNits = 10000f;

    /// <summary>
    /// Returns whether <paramref name="transfer"/> denotes an HDR transfer (PQ / HLG), which
    /// requires the luminance-scaled gamut produced by <see cref="BuildHdrColorSpace"/>.
    /// </summary>
    public static bool IsHdrTransfer(BitmapColorTransfer transfer)
    {
        return transfer is BitmapColorTransfer.Pq or BitmapColorTransfer.Hlg;
    }

    /// <summary>
    /// Resolves the numerical transfer function for a transfer tag. Unknown tags fall back to sRGB.
    /// </summary>
    public static BitmapColorSpaceTransferFn GetTransferFunction(BitmapColorTransfer transfer)
    {
        return transfer switch
        {
            BitmapColorTransfer.Linear => BitmapColorSpaceTransferFn.Linear,
            BitmapColorTransfer.TwoDotTwo => BitmapColorSpaceTransferFn.TwoDotTwo,
            BitmapColorTransfer.Rec2020 => BitmapColorSpaceTransferFn.Rec2020,
            BitmapColorTransfer.Pq => BitmapColorSpaceTransferFn.Pq,
            BitmapColorTransfer.Hlg => BitmapColorSpaceTransferFn.Hlg,
            BitmapColorTransfer.Bt709 => BitmapColorSpaceTransferFn.Bt709,
            BitmapColorTransfer.Gamma28 => BitmapColorSpaceTransferFn.Gamma28,
            BitmapColorTransfer.Smpte240M => BitmapColorSpaceTransferFn.Smpte240M,
            BitmapColorTransfer.Smpte428 => BitmapColorSpaceTransferFn.Smpte428,
            _ => BitmapColorSpaceTransferFn.Srgb,
        };
    }

    /// <summary>
    /// Resolves the toXYZD50 gamut matrix for a primaries tag. Unknown tags fall back to sRGB.
    /// </summary>
    public static BitmapColorSpaceXyz GetPrimaries(BitmapColorPrimaries primaries)
    {
        return primaries switch
        {
            BitmapColorPrimaries.Bt709 => BitmapColorSpaceXyz.Bt709,
            BitmapColorPrimaries.Bt470M => BitmapColorSpaceXyz.Bt470M,
            BitmapColorPrimaries.Bt470BG => BitmapColorSpaceXyz.Bt470BG,
            BitmapColorPrimaries.Smpte170M => BitmapColorSpaceXyz.Smpte170M,
            BitmapColorPrimaries.Smpte240M => BitmapColorSpaceXyz.Smpte240M,
            BitmapColorPrimaries.Film => BitmapColorSpaceXyz.Film,
            BitmapColorPrimaries.Rec2020 => BitmapColorSpaceXyz.Rec2020,
            BitmapColorPrimaries.Xyz => BitmapColorSpaceXyz.Xyz,
            BitmapColorPrimaries.Smpte431 => BitmapColorSpaceXyz.Smpte431,
            BitmapColorPrimaries.Dcip3 => BitmapColorSpaceXyz.Dcip3,
            BitmapColorPrimaries.Ebu3213 => BitmapColorSpaceXyz.Ebu3213,
            _ => BitmapColorSpaceXyz.Srgb,
        };
    }

    /// <summary>
    /// Builds the SDR target color space for a transfer/primaries tag pair, preferring the canonical
    /// <see cref="BitmapColorSpace.Srgb"/> / <see cref="BitmapColorSpace.LinearSrgb"/> instances when
    /// the resolved transfer + gamut match them.
    /// </summary>
    public static BitmapColorSpace BuildTargetColorSpace(BitmapColorTransfer transfer, BitmapColorPrimaries primaries)
    {
        var transferFn = GetTransferFunction(transfer);
        var gamut = GetPrimaries(primaries);

        if (transferFn == BitmapColorSpaceTransferFn.Srgb && gamut == BitmapColorSpaceXyz.Srgb)
            return BitmapColorSpace.Srgb;

        if (transferFn == BitmapColorSpaceTransferFn.Linear && gamut == BitmapColorSpaceXyz.Srgb)
            return BitmapColorSpace.LinearSrgb;

        return BitmapColorSpace.CreateRgb(transferFn, gamut);
    }

    /// <summary>
    /// Builds the HDR color space for a transfer/primaries tag pair. The luminance scale is baked
    /// into the gamut matrix so that internal linear 1.0 maps to reference white (PQ: 203 nit out of
    /// 10000 nit; HLG: the BT.2100 reference code level).
    /// </summary>
    public static BitmapColorSpace BuildHdrColorSpace(BitmapColorTransfer transfer, BitmapColorPrimaries primaries)
    {
        // BT.2100 requires Rec.2020 primaries for HDR; default there when a stream leaves them unspecified.
        if (IsHdrTransfer(transfer) && primaries == BitmapColorPrimaries.Unknown)
            primaries = BitmapColorPrimaries.Rec2020;

        var transferFn = GetTransferFunction(transfer);
        var gamut = GetPrimaries(primaries);

        float scale = GetHdrLuminanceScale(transfer);
        if (scale != 1.0f)
        {
            gamut = gamut.Scale(scale);
        }

        return BitmapColorSpace.CreateRgb(transferFn, gamut);
    }

    /// <summary>
    /// Returns the luminance scale baked into an HDR gamut matrix: <c>10000 / 203</c> for PQ, and
    /// for HLG the reciprocal of the EOTF at the BT.2100 reference code level (0.75), falling back to
    /// the OOTF γ=3 approximation when the EOTF is unusable. Non-HDR transfers return 1.0.
    /// </summary>
    public static float GetHdrLuminanceScale(BitmapColorTransfer transfer)
    {
        return transfer switch
        {
            BitmapColorTransfer.Pq => PqPeakNits / PqReferenceWhiteNits,
            BitmapColorTransfer.Hlg => GetHlgLuminanceScale(),
            _ => 1.0f,
        };
    }

    private static float GetHlgLuminanceScale()
    {
        const float hlgReferenceCode = 0.75f;
        float eotfValue = BitmapColorSpaceTransferFn.Hlg.Transform(hlgReferenceCode);
        if (eotfValue <= 0 || !float.IsFinite(eotfValue))
        {
            // Fallback to the OOTF γ=3 approximation.
            return 18.0f;
        }

        return 1f / eotfValue;
    }
}
