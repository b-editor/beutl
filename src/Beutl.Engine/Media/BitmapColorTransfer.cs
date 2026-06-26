namespace Beutl.Media;

/// <summary>
/// A backend-independent transfer-characteristics tag (a named code point in the spirit of
/// ITU-T H.273). Encode/decode backends map their native transfer tags onto this enum and let
/// <see cref="BitmapColorSpaceMapping"/> resolve the concrete
/// <see cref="BitmapColorSpaceTransferFn"/> and HDR luminance handling, so the mapping logic lives
/// in exactly one place across the FFmpeg / AVFoundation / MediaFoundation paths.
/// </summary>
/// <remarks>
/// The numeric values are part of the wire contract with the AVFoundation native layer
/// (native/BeutlAVF/.../BeutlAVFTypes.h emits these integers into <c>BeutlVideoInfo</c>); do not
/// renumber existing members.
/// </remarks>
public enum BitmapColorTransfer
{
    Unknown = 0,
    Srgb = 1,
    Linear = 2,
    Bt709 = 3,
    Pq = 4,
    Hlg = 5,
    Rec2020 = 6,
    TwoDotTwo = 7,
    Gamma28 = 8,
    Smpte240M = 9,
    Smpte428 = 10,
}
