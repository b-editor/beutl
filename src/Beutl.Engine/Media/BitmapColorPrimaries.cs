namespace Beutl.Media;

/// <summary>
/// A color-primaries tag with Beutl's own numbering, not the ITU-T H.273 code points.
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
