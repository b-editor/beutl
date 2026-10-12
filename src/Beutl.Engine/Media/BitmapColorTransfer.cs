namespace Beutl.Media;

/// <summary>
/// A transfer-characteristics tag with Beutl's own numbering, not the ITU-T H.273 code points.
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
