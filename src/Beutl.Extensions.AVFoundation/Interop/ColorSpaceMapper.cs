using Beutl.Media;

namespace Beutl.Extensions.AVFoundation.Interop;

// Thin AVFoundation layer over the shared Beutl.Media.BitmapColorSpaceMapping. The native side
// already reports Beutl tags (BeutlVideoInfo.TransferFunction / ColorPrimaries, mapped on the
// Swift side), so the only AVF-specific policy left here is the HDR-without-primaries default.
internal static class ColorSpaceMapper
{
    public static BitmapColorSpace BuildColorSpace(
        bool isHdr,
        BitmapColorTransfer transfer,
        BitmapColorPrimaries primaries)
    {
        if (isHdr)
        {
            // For HDR streams without an explicit primaries tag, default to Rec.2020 — that's
            // what the Swift writer stamps (Writer.mapPrimaries) and what both HDR10 and HLG
            // spec-wise assume. Falling back to sRGB here would produce a Rec.2020-tagged file
            // whose pixel values were converted through an sRGB gamut matrix, i.e. wrong colors.
            if (primaries == BitmapColorPrimaries.Unknown)
                primaries = BitmapColorPrimaries.Rec2020;

            return BitmapColorSpaceMapping.BuildHdrColorSpace(transfer, primaries);
        }

        return BitmapColorSpaceMapping.BuildTargetColorSpace(transfer, primaries);
    }
}
