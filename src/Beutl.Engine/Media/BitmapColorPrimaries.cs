namespace Beutl.Media;

/// <summary>
/// A backend-independent color-primaries tag (a named code point in the spirit of ITU-T H.273).
/// Encode/decode backends map their native primaries tags onto this enum and let
/// <see cref="BitmapColorSpaceMapping"/> resolve the concrete <see cref="BitmapColorSpaceXyz"/>
/// gamut, so the mapping logic lives in exactly one place across the
/// FFmpeg / AVFoundation paths.
/// </summary>
/// <remarks>
/// The numeric values are part of the wire contract with the AVFoundation native layer
/// (native/BeutlAVF/.../BeutlAVFTypes.h emits these integers into <c>BeutlVideoInfo</c>); do not
/// renumber existing members.
/// </remarks>
public enum BitmapColorPrimaries
{
    Unknown = 0,
    Srgb = 1,
    Bt709 = 2,
    Bt470M = 3,
    Bt470BG = 4,
    Smpte170M = 5,
    Smpte240M = 6,
    Film = 7,
    Rec2020 = 8,
    Xyz = 9,
    Smpte431 = 10,
    Dcip3 = 11,
    Ebu3213 = 12,
}
