using Beutl.Media;

namespace Beutl.Extensions.AVFoundation.Interop;

// Thin AVFoundation layer over the shared Beutl.Media.BitmapColorSpaceMapping. The native side
// already reports Beutl tags (BeutlVideoInfo.TransferFunction / ColorPrimaries, mapped on the Swift side).
internal static class ColorSpaceMapper
{
    public static BitmapColorSpace BuildColorSpace(
        bool isHdr,
        BitmapColorTransfer transfer,
        BitmapColorPrimaries primaries)
        => isHdr
            ? BitmapColorSpaceMapping.BuildHdrColorSpace(transfer, primaries)
            : BitmapColorSpaceMapping.BuildTargetColorSpace(transfer, primaries);

    // Beutl's default FFmpeg reader (ForceSrgbGamma), like common players, shows BT.709 SDR code values as sRGB,
    // so the encoder keeps sRGB code values there. BT.709 primaries are sRGB's, keeping Skia's canonical sRGB.
    public static BitmapColorSpace BuildSdrOutputColorSpace(BitmapColorTransfer transfer, BitmapColorPrimaries primaries)
        => BitmapColorSpaceMapping.BuildTargetColorSpace(
            transfer == BitmapColorTransfer.Bt709 ? BitmapColorTransfer.Srgb : transfer,
            primaries == BitmapColorPrimaries.Bt709 ? BitmapColorPrimaries.Srgb : primaries);
}
