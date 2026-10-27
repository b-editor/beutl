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
}
