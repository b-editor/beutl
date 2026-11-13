namespace Beutl.Media;

/// <summary>
/// SDR/HDR color-space mapping shared by the encode/decode backends, so the HDR luminance mapping (internal linear 1.0
/// is the 203 nit reference white, for PQ and HLG alike) is defined in one place.
/// </summary>
public static class BitmapColorSpaceMapping
{
    /// <summary>
    /// Returns whether <paramref name="transfer"/> denotes an HDR transfer (PQ / HLG), whose color space comes from
    /// <see cref="BuildHdrColorSpace"/>.
    /// </summary>
    public static bool IsHdrTransfer(BitmapColorTransfer transfer)
    {
        return transfer is BitmapColorTransfer.Pq or BitmapColorTransfer.Hlg;
    }

    /// <summary>
    /// Resolves the numerical transfer function for a transfer tag. Unknown tags fall back to sRGB.
    /// </summary>
    /// <remarks>
    /// BT.709, BT.601 and BT.2020 SDR all tag the camera OETF, whose exact inverse gives scene light rather than what a
    /// display shows. Like QuickTime and AVFoundation, they resolve to
    /// <see cref="BitmapColorSpaceTransferFn.CoreMedia709"/>, so a macOS screen recording reads back as the screen looked,
    /// and an export plays back in QuickTime as it was rendered.
    /// </remarks>
    public static BitmapColorSpaceTransferFn GetTransferFunction(BitmapColorTransfer transfer)
    {
        return transfer switch
        {
            BitmapColorTransfer.Linear => BitmapColorSpaceTransferFn.Linear,
            BitmapColorTransfer.TwoDotTwo => BitmapColorSpaceTransferFn.TwoDotTwo,
            BitmapColorTransfer.Pq => BitmapColorSpaceTransferFn.Pq,
            BitmapColorTransfer.Hlg => BitmapColorSpaceTransferFn.Hlg,
            BitmapColorTransfer.Bt709 or BitmapColorTransfer.Rec2020 => BitmapColorSpaceTransferFn.CoreMedia709,
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
    /// Builds the HDR color space for a transfer/primaries tag pair. Skia converts it so that internal linear 1.0 is
    /// the 203 nit reference white: PQ directly, and HLG through its OOTF for a 1000 nit display, which puts the
    /// BT.2100 reference code level (0.75) there.
    /// </summary>
    public static BitmapColorSpace BuildHdrColorSpace(BitmapColorTransfer transfer, BitmapColorPrimaries primaries)
    {
        // BT.2100 requires Rec.2020 primaries for HDR; default there when a stream leaves them unspecified.
        if (IsHdrTransfer(transfer) && primaries == BitmapColorPrimaries.Unknown)
            primaries = BitmapColorPrimaries.Rec2020;

        return BitmapColorSpace.CreateRgb(GetTransferFunction(transfer), GetPrimaries(primaries));
    }

    /// <summary>
    /// Returns the luminance scale an HDR color space adds to Skia's conversion: 1.0 for every transfer, since Skia
    /// already converts PQ and HLG to the 203 nit reference white.
    /// </summary>
    /// <remarks>
    /// SkiaSharp 4.152 began converting PQ in units of reference white and applying HLG's OOTF with the same
    /// normalization. The scales this used to bake into the gamut (10000/203 for PQ, the inverse of HLG's EOTF at the
    /// reference code level) assumed a Skia that did neither, and made decoded PQ 49 and HLG about 5 times too bright.
    /// </remarks>
    public static float GetHdrLuminanceScale(BitmapColorTransfer transfer)
    {
        return 1.0f;
    }
}
